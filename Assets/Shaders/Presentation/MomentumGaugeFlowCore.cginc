#ifndef PURGERS_MOMENTUM_GAUGE_FLOW_INCLUDED
#define PURGERS_MOMENTUM_GAUGE_FLOW_INCLUDED
float3 MomentumGaugeFlow(float2 uv, float3 tint, float phase, float strength)
{
    float wave = .5 + .5 * cos((uv.x * 3 - phase - uv.y * .22) * 6.2831853);
    float ribbon = pow(wave, 10);
    float fine = pow(.5 + .5 * cos((uv.x * 8 - phase * 2 - uv.y * .4) * 6.2831853), 16);
    float3 flow = tint * (1 - .22 * strength * (1 - wave));
    return lerp(flow, float3(1,1,1), strength * (.24 * ribbon + .07 * fine));
}
#endif
