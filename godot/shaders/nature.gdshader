shader_type spatial;
render_mode diffuse_lambert_wrap, specular_disabled, cull_disabled;

// Painted plants and stones. Vertex colour alpha 1 marks leaves and grass: they sway in the wind
// (more the higher they are) and take the season's tint; snow settles on everything's top.
uniform vec3 leaf_tint = vec3(1.0);
uniform float snow = 0.0;
uniform float wind = 1.0;

varying float leaf;
varying vec3 world_normal;

void vertex() {
	leaf = COLOR.a;
	vec3 world = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	float h = max(VERTEX.y, 0.0);
	float phase = world.x * 0.35 + world.z * 0.27;
	float sway = sin(TIME * 1.3 + phase) * 0.6 + sin(TIME * 2.9 + phase * 1.7) * 0.25;
	VERTEX.x += sway * 0.035 * h * h * wind * (0.3 + 0.7 * leaf);
	VERTEX.z += sway * 0.02 * h * h * wind * (0.3 + 0.7 * leaf);
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	vec3 c = COLOR.rgb;
	c = mix(c, c * leaf_tint, leaf);
	float s = clamp((world_normal.y - 0.35) * 2.0, 0.0, 1.0) * snow;
	c = mix(c, vec3(0.94, 0.96, 0.99), s);
	ALBEDO = c;
	ROUGHNESS = 1.0;
	// Leaves let a little light through, so crowns never turn black on the shadow side.
	BACKLIGHT = vec3(0.25, 0.3, 0.15) * leaf;
}
