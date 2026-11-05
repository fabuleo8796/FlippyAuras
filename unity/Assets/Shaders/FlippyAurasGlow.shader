// Flippy Auras: the one shader every aura draws with. No keywords, so the game's build can't strip a variant we need.
// - Glow: texture × vertex colour × _Intensity (HDR: above 1 makes FIH Ultra's bloom pick it up).
// - Blend is premultiplied (One OneMinusSrcAlpha): _Additive 1 writes alpha 0, so it adds light (sparks, lightning,
//   halos); _Additive 0 is normal see-through blending (ghosts, pictures).
// - _Billboard 1: each vertex sits at its particle's centre and TEXCOORD1 holds its corner offset, which is turned to
//   face whichever camera draws it (the player's view and Tripod Cam's tripod each see the particles head-on).
//   _Billboard 0: the vertices are already where they go (LineRenderer, TrailRenderer, ParticleSystem).
// Built-in style (UnityCG) with no LightMode tag: URP draws it in its SRPDefaultUnlit pass, like the game's own legacy
// particle shader. No URP package needed in the bundle project.
Shader "FlippyAuras/Glow"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Intensity ("Glow", Float) = 1
        _Additive ("Additive", Float) = 1
        _Billboard ("Billboard", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Intensity, _Additive, _Billboard;

            struct Attributes
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 corner : TEXCOORD1;
            };

            struct Varyings
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 world = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1)).xyz;
                // the camera's right and up, from the view matrix's first two rows
                float3 right = float3(UNITY_MATRIX_V[0].x, UNITY_MATRIX_V[0].y, UNITY_MATRIX_V[0].z);
                float3 up = float3(UNITY_MATRIX_V[1].x, UNITY_MATRIX_V[1].y, UNITY_MATRIX_V[1].z);
                world += (right * v.corner.x + up * v.corner.y) * _Billboard;
                o.pos = mul(UNITY_MATRIX_VP, float4(world, 1));
                o.color = v.color;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float4 c = tex2D(_MainTex, i.uv) * i.color;
                float3 rgb = c.rgb * c.a * _Intensity;
                return float4(rgb, c.a * (1 - _Additive));
            }
            ENDCG
        }
    }
}
