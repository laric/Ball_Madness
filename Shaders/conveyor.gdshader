// Conveyor belt: chevrons that scroll the way the belt carries the ball.
// Conveyor.cs lines the stripes up with the belt.
shader_type spatial;

uniform vec4 base_color : source_color = vec4(0.16, 0.17, 0.2, 1.0);
uniform vec4 stripe_color : source_color = vec4(0.95, 0.75, 0.15, 1.0);
uniform vec3 belt_dir = vec3(0.0, 0.0, 1.0);
uniform vec3 belt_side = vec3(1.0, 0.0, 0.0);
uniform vec3 center = vec3(0.0);
uniform float speed = 4.0;
uniform float spacing = 1.0;

varying vec3 world_pos;
varying vec3 world_normal;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	vec3 p = world_pos - center;
	float along = dot(p, belt_dir);
	float across = dot(p, belt_side);
	// Chevrons point down the belt.
	float s = (along + abs(across) * 0.6) / spacing - TIME * speed / spacing;
	float stripe = smoothstep(0.42, 0.5, abs(fract(s) - 0.5));
	float top = smoothstep(0.3, 0.9, abs(world_normal.y));
	vec3 color = mix(base_color.rgb, stripe_color.rgb, stripe * top);
	color *= mix(0.65, 1.0, top);
	ALBEDO = color;
	ROUGHNESS = 0.7;
}
