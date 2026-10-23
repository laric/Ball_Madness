// Two-tone octant pattern in object space, so you can see the ball roll.
shader_type spatial;

uniform vec4 color_a : source_color = vec4(0.95, 0.2, 0.25, 1.0);
uniform vec4 color_b : source_color = vec4(0.97, 0.95, 0.9, 1.0);

varying vec3 local_pos;

void vertex() {
	local_pos = VERTEX;
}

void fragment() {
	float s = sign(local_pos.x) * sign(local_pos.y) * sign(local_pos.z);
	ALBEDO = s > 0.0 ? color_a.rgb : color_b.rgb;
	ROUGHNESS = 0.25;
	METALLIC = 0.1;
}
