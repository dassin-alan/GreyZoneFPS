// GreyZone - lightweight vertex-colour lit shader (Built-in RP, ForwardBase only, no shadows).
Shader "GreyZone/VertexColorLit"
{
    Properties
    {
        _MainTex ("Grain (RGB)", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _AmbientBoost ("Ambient Boost", Range(0,2)) = 1
        _LightBoost ("Light Boost", Range(0,2)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            Tags { "LightMode"="ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos         : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float4 color       : COLOR;
                float2 uv          : TEXCOORD1;
                UNITY_FOG_COORDS(2)
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Tint;
            float _AmbientBoost;
            float _LightBoost;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.color = v.color * _Tint;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.worldNormal);
                float3 l = normalize(_WorldSpaceLightPos0.xyz);
                float ndl = saturate(dot(n, l)) * 0.65 + 0.35;
                float3 grain = tex2D(_MainTex, i.uv).rgb;
                float3 ambient = UNITY_LIGHTMODEL_AMBIENT.rgb * _AmbientBoost;
                float3 lit = i.color.rgb * grain * (ambient + _LightColor0.rgb * ndl * _LightBoost);
                fixed4 c = fixed4(lit, i.color.a);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
    Fallback "Legacy Shaders/Diffuse"
}
