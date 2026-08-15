#ifndef RACER_ROAD_GRASS_CLIP_INCLUDED
#define RACER_ROAD_GRASS_CLIP_INCLUDED

TEXTURE2D(_RacerRoadGrassClipTex);
SAMPLER(sampler_RacerRoadGrassClipTex);

CBUFFER_START(RacerRoadGrassClip)
    float4 _RacerRoadGrassClipMinMax;
CBUFFER_END

void ClipIfOnRoad(float3 positionWS)
{
#if defined(RACER_ROAD_GRASS_CLIP)
    float2 mn = _RacerRoadGrassClipMinMax.xy;
    float2 mx = _RacerRoadGrassClipMinMax.zw;
    float2 ext = mx - mn;
    if (ext.x < 1e-4 || ext.y < 1e-4)
        return;

    float2 uv = (positionWS.xz - mn) / ext;
    if (any(uv < 0.0) || any(uv > 1.0))
        return;

    float mask = SAMPLE_TEXTURE2D_LOD(_RacerRoadGrassClipTex, sampler_RacerRoadGrassClipTex, uv, 0).r;
    clip(0.5 - mask);
#endif
}

#endif
