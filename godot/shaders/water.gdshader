shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled, specular_schlick_ggx;

// Painted water: deep teal in the middle, light at the shore (from the depth behind the surface),
// slow ripples and a soft sky glint.
uniform vec3 shallow : source_color = vec3(0.45, 0.72, 0.72);
uniform vec3 deep : source_color = vec3(0.16, 0.38, 0.47);
uniform sampler2D ripples : hint_normal, filter_linear_mipmap, repeat_enable;
uniform sampler2D depth_tex : hint_depth_texture, filter_linear;
uniform float night = 0.0;
uniform float frozen = 0.0;

void fragment() {
	vec2 uv1 = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xz * 0.06 + TIME * vec2(0.010, 0.006);
	vec2 uv2 = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xz * 0.11 - TIME * vec2(0.007, 0.012);
	vec3 n = normalize(texture(ripples, uv1).rgb + texture(ripples, uv2).rgb - 1.0);
	NORMAL_MAP = n * 0.5 + 0.5;
	NORMAL_MAP_DEPTH = 0.35;

	float depth_raw = texture(depth_tex, SCREEN_UV).r;
	vec4 ndc = vec4(SCREEN_UV * 2.0 - 1.0, depth_raw, 1.0);
	vec4 behind = INV_PROJECTION_MATRIX * ndc;
	behind.xyz /= behind.w;
	float thickness = clamp((VERTEX.z - behind.z) * 0.7, 0.0, 1.0);

	vec3 c = mix(shallow, deep, thickness);
	c = mix(c, vec3(0.86, 0.92, 0.96), frozen * 0.85);
	c *= 1.0 - night * 0.55;
	ALBEDO = c;
	ALPHA = mix(0.55, 0.92, thickness);
	ROUGHNESS = mix(0.12, 0.6, frozen);
	METALLIC = 0.0;
	SPECULAR = 0.35;
}
