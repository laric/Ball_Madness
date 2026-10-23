// World-space grid for course surfaces. The grid lines make speed and slope
// readable from the isometric camera, Marble Madness style.
shader_type spatial;

uniform vec4 base_color : source_color = vec4(0.55, 0.62, 0.75, 1.0);
uniform vec4 line_color : source_color = vec4(0.18, 0.2, 0.3, 1.0);
uniform float cell_size = 1.0;
uniform float side_darken : hint_range(0.0, 1.0) = 0.35;

varying vec3 world_pos;
varying vec3 world_normal;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	vec3 p = world_pos / cell_size;
	vec3 n = abs(world_normal);
	vec2 uv = n.y > 0.5 ? p.xz : (n.x > n.z ? p.zy : p.xy);

	vec2 dist = abs(fract(uv - 0.5) - 0.5) / fwidth(uv);
	float line = 1.0 - min(min(dist.x, dist.y), 1.0);

	vec3 color = mix(base_color.rgb, line_color.rgb, line);
	color *= mix(1.0 - side_darken, 1.0, smoothstep(0.3, 0.9, n.y));

	ALBEDO = color;
	ROUGHNESS = 0.85;
}
