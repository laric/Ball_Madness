// World-space grid for course surfaces. The grid lines make speed and slope
// readable from the isometric camera, Marble Madness style.
//
// A world theme can also give a surface a procedural texture (pattern):
// grass, mossy cobbles, sand, flagstones, planks, ice, basalt, metal plates
// or crystal. Everything is worked out from the world position, so
// neighbouring parts (and a bump on a floor) join up seamlessly, and no
// image files are needed.
shader_type spatial;

uniform vec4 base_color : source_color = vec4(0.55, 0.62, 0.75, 1.0);
uniform vec4 line_color : source_color = vec4(0.18, 0.2, 0.3, 1.0);
uniform float cell_size = 1.0;
uniform float side_darken : hint_range(0.0, 1.0) = 0.35;
// Neon: the grid lines (and a pattern's accents, such as basalt's cracks)
// give off their colour at this strength.
uniform float line_glow = 0.0;
// How strongly the grid lines show: 1 for plain surfaces, softer on textures.
uniform float line_strength = 1.0;

// 0 none, 1 grass, 2 moss, 3 sand, 4 stone, 5 planks, 6 ice, 7 basalt,
// 8 metal, 9 crystal. Keep in step with LevelBuilder.Patterns.
uniform int pattern = 0;
// The pattern's second colour: soil, moss, mortar, cracks, seams.
uniform vec4 detail_color : source_color = vec4(0.3, 0.3, 0.3, 1.0);
// Metres per pattern unit: above 1 makes the pattern bigger.
uniform float texture_scale = 1.0;
uniform float roughness = 0.85;
uniform float metallic = 0.0;

varying vec3 world_pos;
varying vec3 world_normal;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

float hash12(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

vec2 hash22(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * vec3(0.1031, 0.1030, 0.0973));
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.xx + p3.yz) * p3.zy);
}

float vnoise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash12(i), hash12(i + vec2(1.0, 0.0)), u.x),
		mix(hash12(i + vec2(0.0, 1.0)), hash12(i + vec2(1.0, 1.0)), u.x), u.y);
}

float fbm(vec2 p) {
	float sum = 0.0;
	float amp = 0.5;
	for (int i = 0; i < 4; i++) {
		sum += amp * vnoise(p);
		p = p * 2.03 + vec2(17.1, 9.7);
		amp *= 0.5;
	}
	return sum / 0.9375;
}

// x: distance to the nearest cell point, y: distance to the nearest cell
// edge, z: a random number for the cell.
vec3 voronoi(vec2 p) {
	vec2 n = floor(p);
	vec2 f = fract(p);
	vec2 mg = vec2(0.0);
	vec2 mr = vec2(0.0);
	float md = 8.0;
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 g = vec2(float(i), float(j));
			vec2 r = g + hash22(n + g) - f;
			float d = dot(r, r);
			if (d < md) {
				md = d;
				mr = r;
				mg = g;
			}
		}
	}
	float edge = 8.0;
	for (int j = -2; j <= 2; j++) {
		for (int i = -2; i <= 2; i++) {
			vec2 g = mg + vec2(float(i), float(j));
			vec2 r = g + hash22(n + g) - f;
			if (dot(mr - r, mr - r) > 0.00001) {
				edge = min(edge, dot(0.5 * (mr + r), normalize(r - mr)));
			}
		}
	}
	return vec3(sqrt(md), edge, hash12(n + mg));
}

// 1 where detail of this many cycles a metre is big enough on screen to
// show, fading to 0 where it would only shimmer.
float fade(float freq, vec2 t) {
	return 1.0 - smoothstep(0.25, 0.6, freq * length(fwidth(t)));
}

// A line of `width` along voronoi edges at distance `d`, antialiased.
float edge_line(float d, float width, float freq, vec2 t) {
	float aa = max(freq * length(fwidth(t)) * 0.7, 0.002);
	return 1.0 - smoothstep(width - aa, width + aa, d);
}

void fragment() {
	vec3 p = world_pos / cell_size;
	vec3 n = abs(world_normal);
	vec2 uv = n.y > 0.5 ? p.xz : (n.x > n.z ? p.zy : p.xy);

	vec2 dist = abs(fract(uv - 0.5) - 0.5) / fwidth(uv);
	float line = 1.0 - min(min(dist.x, dist.y), 1.0);

	vec3 base = base_color.rgb;
	vec3 detail = detail_color.rgb;
	// 1 on floors, 0 on walls and the sides of blocks.
	float top = smoothstep(0.4, 0.75, world_normal.y);
	vec3 wp = world_pos / texture_scale;
	vec2 t = n.y > 0.5 ? wp.xz : (n.x > n.z ? wp.zy : wp.xy);
	// Walls get their own patch of pattern rather than a copy of the floor's.
	// (A step, not a blend, or slopes would smear it.)
	t += n.y > 0.5 ? vec2(0.0) : vec2(31.7, 12.3);

	vec3 color = base;
	float accent = 0.0;

	if (pattern == 1) {
		// Grass: patchy green on top, soil down the sides.
		float patches = fbm(t * 0.9);
		float tufts = vnoise(t * 6.0 + patches * 2.0);
		vec3 grass = base * (0.7 + 0.6 * patches);
		grass = mix(grass, grass * 1.25, smoothstep(0.55, 0.85, tufts) * fade(6.0, t));
		grass = mix(grass, grass * 0.72, smoothstep(0.55, 0.9, vnoise(t * 2.5 + 7.0)) * 0.7);
		grass = mix(grass, detail, smoothstep(0.72, 0.9, fbm(t * 0.35 + 11.0)) * 0.55);
		vec3 soil = detail * (0.8 + 0.4 * fbm(t * 2.0));
		color = mix(soil, grass, top);
	} else if (pattern == 2) {
		// Mossy cobbles: stones with dark joints, moss creeping over the top.
		vec3 v = voronoi(t * 1.7);
		vec3 stone = base * (0.72 + 0.5 * v.z) * (0.85 + 0.3 * fbm(t * 3.0));
		float joint = edge_line(v.y, 0.06, 1.7, t);
		color = mix(stone, base * 0.35, joint);
		float moss = smoothstep(0.5, 0.68, fbm(t * 0.8 + 3.1) * mix(0.6, 1.0, top) + top * 0.08 + joint * 0.12);
		vec3 moss_color = detail * (0.75 + 0.45 * vnoise(t * 5.0) * fade(5.0, t) + 0.2 * (1.0 - fade(5.0, t)));
		color = mix(color, moss_color, moss);
	} else if (pattern == 3) {
		// Sand: soft dunes of ripples and a fine grain.
		float drift = fbm(t * 0.5);
		float ripple = sin(t.x * 4.0 + t.y * 1.3 + drift * 7.0);
		color = base * (0.92 + 0.14 * fbm(t * 1.7));
		color = mix(color, detail, smoothstep(0.55, 1.0, ripple) * 0.4 * top * fade(4.0, t));
		color *= 0.94 + 0.12 * vnoise(t * 7.0) * fade(7.0, t);
	} else if (pattern == 4) {
		// Flagstones with mortar between.
		vec3 v = voronoi(t * 1.05);
		color = base * (0.8 + 0.35 * v.z) * (0.88 + 0.24 * fbm(t * 2.6));
		color = mix(color, detail, edge_line(v.y, 0.05, 1.05, t));
	} else if (pattern == 5) {
		// Planks: half-metre boards, staggered joints, grain along the board.
		float board = floor(t.x / 0.5);
		float along = t.y + hash12(vec2(board, 3.0)) * 7.0;
		float piece = floor(along / 3.0);
		float tint = hash12(vec2(board, piece));
		float grain = vnoise(vec2(t.x * 9.0, along * 0.9)) * fade(9.0, t);
		color = base * (0.8 + 0.32 * tint) * (0.86 + 0.28 * grain);
		float fx = fract(t.x / 0.5);
		float fy = fract(along / 3.0);
		float aa = length(fwidth(t)) * 1.5;
		float gap = max(1.0 - smoothstep(0.02, 0.02 + aa * 2.0, min(fx, 1.0 - fx)),
			1.0 - smoothstep(0.004, 0.004 + aa * 0.33, min(fy, 1.0 - fy)));
		color = mix(color, detail, gap);
	} else if (pattern == 6) {
		// Ice: cloudy, with fine white cracks.
		color = mix(base, detail, fbm(t * 0.7) * 0.45);
		vec3 v1 = voronoi(t * 0.6);
		vec3 v2 = voronoi(t * 1.5 + 4.0);
		float crack = max(edge_line(v1.y, 0.012, 0.6, t), edge_line(v2.y, 0.01, 1.5, t) * 0.5);
		color = mix(color, detail * 1.15, crack * 0.8);
	} else if (pattern == 7) {
		// Basalt: dark cooled rock, glowing cracks.
		vec3 v = voronoi(t * 1.3);
		color = base * (0.7 + 0.55 * v.z) * (0.8 + 0.4 * fbm(t * 2.8));
		float crack = edge_line(v.y, 0.045, 1.3, t);
		accent = crack * (0.55 + 0.45 * fbm(t * 1.5 + vec2(TIME * 0.12, 0.0)));
		color = mix(color, detail, accent);
	} else if (pattern == 8) {
		// Metal plates: 2 m panels with seams, rivets at the corners and a brushed finish.
		vec2 q = t / 2.0;
		vec2 f = fract(q);
		vec2 corner = min(f, 1.0 - f);
		float aa = length(fwidth(q)) * 1.5;
		float seam = 1.0 - smoothstep(0.006, 0.006 + aa, min(corner.x, corner.y));
		float rivet = 1.0 - smoothstep(0.022, 0.03, length(corner - 0.05));
		float tint = hash12(floor(q));
		float brushed = vnoise(t * vec2(1.0, 10.0)) * fade(10.0, t);
		color = base * (0.86 + 0.24 * tint) * (0.95 + 0.1 * brushed);
		color = mix(color, detail, seam);
		color = mix(color, detail * 1.4, rivet);
	} else if (pattern == 9) {
		// Crystal: facets that catch the light differently, bright edges.
		vec3 v = voronoi(t * 1.5);
		color = base * (0.55 + 0.85 * v.z);
		accent = edge_line(v.y, 0.035, 1.5, t);
		color = mix(color, detail, accent);
	}

	color = mix(color, line_color.rgb, line * line_strength);
	color *= mix(1.0 - side_darken, 1.0, smoothstep(0.3, 0.9, n.y));

	ALBEDO = color;
	EMISSION = line_color.rgb * line * line_strength * line_glow + detail * accent * line_glow;
	ROUGHNESS = roughness;
	METALLIC = metallic;
}
