// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.
//
// The C entry point of cantoria_world: WORLD's analysis (Harvest, CheapTrick, D4C) and synthesis
// over flat arrays, for Cantoria.Core's WorldNative. A spectrogram or aperiodicity is frames rows
// of fft_size / 2 + 1 values, one row after another.

#include <cstddef>
#include <vector>

#include "world/cheaptrick.h"
#include "world/d4c.h"
#include "world/harvest.h"
#include "world/synthesis.h"

#if defined(_WIN32)
#define CANTORIA_EXPORT extern "C" __declspec(dllexport)
#else
#define CANTORIA_EXPORT extern "C" __attribute__((visibility("default")))
#endif

namespace {

std::vector<double*> Rows(double* flat, int frames, int width) {
  std::vector<double*> rows(frames);
  for (int i = 0; i < frames; ++i) rows[i] = flat + static_cast<std::size_t>(i) * width;
  return rows;
}

std::vector<const double*> Rows(const double* flat, int frames, int width) {
  std::vector<const double*> rows(frames);
  for (int i = 0; i < frames; ++i) rows[i] = flat + static_cast<std::size_t>(i) * width;
  return rows;
}

}  // namespace

// The frames an analysis of length samples gives.
CANTORIA_EXPORT int cantoria_world_frames(int fs, int length, double frame_period) {
  return GetSamplesForHarvest(fs, length, frame_period);
}

// The FFT size the analysis uses at this sample rate; a row is fft_size / 2 + 1 values.
CANTORIA_EXPORT int cantoria_world_fft_size(int fs) {
  CheapTrickOption option;
  InitializeCheapTrickOption(fs, &option);
  return option.fft_size;
}

// Analyses x: f0 (Hz, 0 where unvoiced; frames values), sp and ap (frames rows each).
// Returns the frames written, or -1 when frames is not what cantoria_world_frames gives.
CANTORIA_EXPORT int cantoria_world_analyze(const double* x, int length, int fs, double frame_period,
                                           double f0_floor, double f0_ceil, int frames,
                                           double* f0, double* sp, double* ap) {
  if (frames != GetSamplesForHarvest(fs, length, frame_period)) return -1;
  std::vector<double> positions(frames);
  HarvestOption harvest;
  InitializeHarvestOption(&harvest);
  harvest.frame_period = frame_period;
  harvest.f0_floor = f0_floor;
  harvest.f0_ceil = f0_ceil;
  Harvest(x, length, fs, &harvest, positions.data(), f0);

  CheapTrickOption cheaptrick;
  InitializeCheapTrickOption(fs, &cheaptrick);
  int width = cheaptrick.fft_size / 2 + 1;
  auto sp_rows = Rows(sp, frames, width);
  CheapTrick(x, length, fs, positions.data(), f0, frames, &cheaptrick, sp_rows.data());

  // D4C's own voiced/unvoiced judgement (LoveTrain, threshold 0.85) is off: Harvest has judged it.
  // On Kasane Teto's VCV "u き" it called the loud, steady second half of the い aperiodic, which
  // was sung as breath (2026-11-28).
  D4COption d4c;
  InitializeD4COption(&d4c);
  d4c.threshold = 0.0;
  auto ap_rows = Rows(ap, frames, width);
  D4C(x, length, fs, positions.data(), f0, frames, cheaptrick.fft_size, &d4c, ap_rows.data());
  return frames;
}

// Synthesises length samples into y from frames of f0, sp and ap.
CANTORIA_EXPORT int cantoria_world_synthesize(const double* f0, int frames, const double* sp, const double* ap,
                                              int fft_size, double frame_period, int fs, int length, double* y) {
  int width = fft_size / 2 + 1;
  auto sp_rows = Rows(sp, frames, width);
  auto ap_rows = Rows(ap, frames, width);
  Synthesis(f0, frames, sp_rows.data(), ap_rows.data(), fft_size, frame_period, fs, length, y);
  return length;
}
