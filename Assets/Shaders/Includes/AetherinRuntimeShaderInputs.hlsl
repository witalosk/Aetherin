#ifndef AETHERIN_RUNTIME_SHADER_INPUTS_INCLUDED
#define AETHERIN_RUNTIME_SHADER_INPUTS_INCLUDED

// x: elapsed seconds, y: delta time, z: sin(time), w: cos(time)
float4 _AetherinTime;
// x: frame count, y: time scale, z: unscaled time, w: unscaled delta time
float4 _AetherinFrame;
// xy: render resolution, zw: inverse resolution
float4 _AetherinResolution;
// xy: evaluated quad size, z: aspect ratio, w: opacity
float4 _AetherinQuad;
// x: input RMS, y: kick, z: snare/clap, w: transient this frame
float4 _AetherinAudio;
// x: beat phase, y: beat count, z: beat in bar, w: beat this frame
float4 _AetherinBeat;
// x: bar phase, y: bar count, z: beats per bar, w: bar this frame
float4 _AetherinBar;
float _AetherinOpacity;

TEXTURE2D(_AetherinWaveformTex);
SAMPLER(sampler_AetherinWaveformTex);
TEXTURE2D(_AetherinSpectrumTex);
SAMPLER(sampler_AetherinSpectrumTex);

float4 _BackgroundColor1;
float4 _BackgroundColor2;
float4 _AccentColor1;
float4 _AccentColor2;
float4 _SubAccentColor1;
float4 _SubAccentColor2;

float _UserFloat0;
float _UserFloat1;
float _UserFloat2;
float _UserFloat3;
float4 _UserVector0;
float4 _UserVector1;
float4 _UserVector2;
float4 _UserVector3;

#endif
