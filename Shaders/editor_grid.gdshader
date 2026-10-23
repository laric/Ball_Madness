// The level editor's build grid: faint lines every 0.5 m, stronger every 2 m,
// fading out away from the camera's focus.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled;

uniform vec4 minor_color : source_color = vec4(0.85, 0.9, 1.0, 0.18);
uniform vec4 major_color : source_color = vec4(0.85, 0.9, 1.0, 0.45);
uniform vec4 fill_color : source_color = vec4(0.4, 0.5, 0.8, 0.06);
uniform float cell = 0.5;
uniform float major_every = 4.0;
uniform vec3 fade_center = vec3(0.0);
uniform float fade_radius = 60.0;

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

float grid_line(vec2 uv) {
	vec2 dist = abs(fract(uv - 0.5) - 0.5) / fwidth(uv);
	return 1.0 - min(min(dist.x, dist.y), 1.0);
}

void fragment() {
	vec2 uv = world_pos.xz / cell;
	float minor = grid_line(uv);
	float major = grid_line(uv / major_every);
	float fade = 1.0 - smoothstep(fade_radius * 0.5, fade_radius, distance(world_pos.xz, fade_center.xz));

	vec4 color = fill_color;
	color = mix(color, minor_color, minor);
	color = mix(color, major_color, major);
	ALBEDO = color.rgb;
	ALPHA = color.a * fade;
}
