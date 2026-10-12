#version 450

// Vanilla's Wound shader with one extra step: the wound art is reduced to its brightest
// channel before the tint is applied, so red-painted wounds take any blood color cleanly.
// Everything else (mask handling, rotation, cutoff) is kept identical to Wound.frag.glsl
// so the wound drawer can treat this exactly like the vanilla shader.

layout(location = 1) in vec2 vUv;

layout(location = 0) out vec4 outColor;

layout(set = 2, binding = 0) uniform MaterialUniforms
{
    vec4 _Color;           // @color @default = vec4(1.0, 1.0, 1.0, 1.0)
    vec4 _Main_TexOffset;  // @default = vec4(0.0, 0.0, 0.0, 0.0)
    vec4 _Main_TexScale;   // @default = vec4(1.0, 1.0, 1.0, 1.0)
    vec4 _Mask_TexOffset;  // @default = vec4(0.0, 0.0, 0.0, 0.0)
    vec4 _Mask_TexScale;   // @default = vec4(1.0, 1.0, 1.0, 1.0)
    float _Rotation;       // @default = 0.0
} uMaterial;

layout(set = 2, binding = 1) uniform sampler2D _MainTex;
layout(set = 2, binding = 2) uniform sampler2D _MaskTex;

vec2 flipV(vec2 c) { return vec2(c.x, 1.0 - c.y); }

vec2 rotateUV(vec2 uv, float rotation)
{
    float mid = 0.5;
    return vec2(
        cos(rotation) * (uv.x - mid) + sin(rotation) * (uv.y - mid) + mid,
        cos(rotation) * (uv.y - mid) - sin(rotation) * (uv.x - mid) + mid);
}

void main()
{
    vec2 uv = vUv * uMaterial._Mask_TexScale.xy + uMaterial._Mask_TexOffset.xy;
    uv.x = mix(uv.x, 1.0 - uv.x, uMaterial._Mask_TexOffset.z); // z flags an x-flipped mask graphic
    vec4 maskSample = texture(_MaskTex, flipV(uv));
    float value = dot(maskSample.rgb, vec3(0.299, 0.587, 0.114)); // dark outlines suppress the scar

    vec2 xy = vUv / uMaterial._Main_TexScale.xy + uMaterial._Main_TexOffset.xy;
    vec4 c = texture(_MainTex, flipV(rotateUV(xy, uMaterial._Rotation)));
    c.rgb = vec3(max(c.r, max(c.g, c.b))); // brightest channel, so the art's own hue is dropped
    c *= uMaterial._Color;
    c.a *= maskSample.a * value;

    if (c.a < 0.1)
        discard;
    outColor = c;
}
