shader_type spatial;
render_mode diffuse_lambert_wrap, specular_disabled;

// Painted ground: the vertex colour carries the ground layers (mud, sand, rock, meadow), alpha says
// how much meadow is there so the season can tint it. A cellular brush texture breaks up the colour
// like paint strokes; slopes darken a little so the relief reads from above.
uniform sampler2D brush : source_color, filter_linear_mipmap, repeat_enable;
uniform vec3 season_tint = vec3(1.0);
uniform float snow = 0.0;

varying vec3 world_pos;
varying vec3 world_normal;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	vec3 base = COLOR.rgb;
	float meadow = COLOR.a;
	float b1 = texture(brush, world_pos.xz * 0.045).r;
	float b2 = texture(brush, world_pos.xz * 0.19 + vec2(0.37, 0.71)).r;
	vec3 c = base * (0.9 + 0.12 * b1 + 0.08 * b2);
	c = mix(c, c * season_tint, meadow);
	float slope = 1.0 - clamp(world_normal.y, 0.0, 1.0);
	c *= 1.0 - slope * 0.25;
	// Snow settles on flat ground first.
	float s = clamp(snow * (1.15 - slope * 1.6) + (b2 - 0.5) * 0.3 * snow, 0.0, 1.0);
	c = mix(c, vec3(0.93, 0.95, 0.98), s);
	ALBEDO = c;
	ROUGHNESS = 1.0;
	SPECULAR = 0.0;
}
