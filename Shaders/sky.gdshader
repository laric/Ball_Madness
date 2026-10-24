// The world theme's background. The game's camera is orthographic, so every
// pixel looks the same way: the gradient runs down the screen instead of
// up the sky. The light the sky gives off (the cubemap pass) uses the view
// direction as usual.
shader_type sky;

uniform vec3 top_color : source_color = vec3(0.08, 0.06, 0.2);
uniform vec3 horizon_color : source_color = vec3(0.35, 0.3, 0.55);
uniform vec3 ground_color : source_color = vec3(0.02, 0.02, 0.05);
uniform float stars : hint_range(0.0, 1.0) = 0.0;

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

void sky() {
	if (AT_CUBEMAP_PASS) {
		float y = EYEDIR.y;
		COLOR = y > 0.0 ? mix(horizon_color, top_color, sqrt(y)) : mix(horizon_color, ground_color, sqrt(-y));
	} else {
		vec3 color = mix(top_color, horizon_color, smoothstep(0.0, 1.0, SCREEN_UV.y));
		if (stars > 0.0) {
			// One possible star per 6 x 6 pixel cell.
			vec2 grid = FRAGCOORD.xy / 6.0;
			vec2 cell = floor(grid);
			float h = hash(cell);
			if (h > 1.0 - 0.06 * stars) {
				vec2 centre = vec2(hash(cell + 1.7), hash(cell + 4.3)) * 0.6 + 0.2;
				float d = length(fract(grid) - centre);
				float brightness = hash(cell + 9.1);
				color += vec3(smoothstep(0.18, 0.0, d) * (0.4 + 0.8 * brightness));
			}
		}
		COLOR = color;
	}
}
