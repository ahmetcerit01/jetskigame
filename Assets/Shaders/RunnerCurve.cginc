#ifndef RUNNER_CURVE_INCLUDED
#define RUNNER_CURVE_INCLUDED

// Curved world: everything ahead of the camera drops with the square of its distance, so the run seems to
// roll over the top of a small planet. Only what is drawn moves; game logic and collisions stay on a flat world.
// The drop stops growing at _CurveEnd so the far mountains stay above the horizon.
float _CurveAmount, _CurveEnd;

inline float3 CurveWorld(float3 world)
{
    float d = clamp(world.z - _WorldSpaceCameraPos.z, 0.0, _CurveEnd);
    world.y -= _CurveAmount * d * d;
    return world;
}

#endif
