// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.
//
// The C entry point Cantoria.Core's SinsySinger calls (P/Invoke). A handle is Sinsy, which turns
// a score built note by note (durations in its ticks, 960 a quarter note) into full-context
// labels with its Japanese dictionary, and an hts_engine with one voice, which sings the labels.
// The engine is driven here rather than through Sinsy's synthesize so that the pitch can be moved
// as a whole (hts_engine's additional half tone): an HMM voice sings only the range it was
// trained on, so a phrase below it is given to Sinsy higher and brought back down here.
// The samples come out as floats in -1..1.
//
// Sinsy writes its log to std::cout and its errors to std::cerr; Cantoria's `serve` speaks JSON
// lines on stdout, so both are caught for the length of each call and kept for
// cantoria_sinsy_messages. One call at a time in the process (the streams are global).

#include <cstring>
#include <iostream>
#include <memory>
#include <mutex>
#include <sstream>
#include <string>
#include <vector>

#include "sinsy.h"
#include "HTS_engine.h"

#if defined(_WIN32)
#define CS_API extern "C" __declspec(dllexport)
#else
#define CS_API extern "C" __attribute__((visibility("default")))
#endif

namespace
{
std::mutex gate;

struct Handle
{
   sinsy::Sinsy sinsy;
   HTS_Engine engine;
   bool loaded = false;
   std::vector<float> samples;
   std::string messages;

   Handle() { HTS_Engine_initialize(&engine); }
   ~Handle() { HTS_Engine_clear(&engine); }
};

// Sinsy's std::cout and std::cerr into the messages while it lives.
class Caught
{
public:
   explicit Caught(std::string& into) : target(into), lock(gate), out(std::cout.rdbuf(text.rdbuf())), err(std::cerr.rdbuf(text.rdbuf())) {}
   ~Caught()
   {
      std::cout.rdbuf(out);
      std::cerr.rdbuf(err);
      target = text.str();
   }
   void say(const std::string& words) { text << words << "\n"; }
private:
   std::string& target;
   std::lock_guard<std::mutex> lock;
   std::ostringstream text;
   std::streambuf* out;
   std::streambuf* err;
};

std::string lastOpenError;
}

// A handle that sings Japanese (the dictionary folder holds japanese.utf_8.table and .conf) with
// the voice (.htsvoice); null when either cannot be read, and the words in
// cantoria_sinsy_last_open_error.
CS_API void* cantoria_sinsy_open(const char* dictionaryFolder, const char* voiceFile)
{
   std::unique_ptr<Handle> h(new Handle());
   bool ok;
   {
      Caught c(lastOpenError);
      ok = h->sinsy.setLanguages("j", dictionaryFolder) && h->sinsy.setEncoding("utf_8");
      if (!ok) {
         c.say(std::string("Sinsy could not read its dictionary in ") + dictionaryFolder);
      } else {
         char* files[1] = { const_cast<char*>(voiceFile) };
         ok = HTS_Engine_load(&h->engine, files, 1) == TRUE;
         if (!ok) {
            c.say(std::string("hts_engine could not read ") + voiceFile);
         } else {
            h->loaded = true;
            HTS_Engine_set_audio_buff_size(&h->engine, 0);   // no sound card: the samples are taken
            HTS_Engine_set_phoneme_alignment_flag(&h->engine, TRUE);   // the labels' times are Sinsy's notes
         }
      }
   }
   return ok ? h.release() : nullptr;
}

CS_API const char* cantoria_sinsy_last_open_error()
{
   return lastOpenError.c_str();
}

CS_API void cantoria_sinsy_close(void* handle)
{
   delete static_cast<Handle*>(handle);
}

CS_API int cantoria_sinsy_sample_rate(void* handle)
{
   return static_cast<int>(HTS_Engine_get_sampling_frequency(&static_cast<Handle*>(handle)->engine));
}

// What was written during the last call on this handle (Sinsy's log and errors).
CS_API const char* cantoria_sinsy_messages(void* handle)
{
   return static_cast<Handle*>(handle)->messages.c_str();
}

CS_API int cantoria_sinsy_clear(void* handle)
{
   Handle* h = static_cast<Handle*>(handle);
   Caught c(h->messages);
   return h->sinsy.clearScore() ? 1 : 0;
}

CS_API int cantoria_sinsy_tempo(void* handle, double bpm)
{
   Handle* h = static_cast<Handle*>(handle);
   Caught c(h->messages);
   return h->sinsy.addTempoMark(bpm) ? 1 : 0;
}

// pitch: Sinsy's, octave * 12 + step (C4 is 48); tie, slur: 0 none, 1 begin, 2 end;
// syllabic: 0 single, 1 begin, 2 middle, 3 end. The lyric is UTF-8.
CS_API int cantoria_sinsy_note(void* handle, int duration, const char* lyric, int pitch, int tie, int slur, int syllabic, int breath)
{
   Handle* h = static_cast<Handle*>(handle);
   Caught c(h->messages);
   return h->sinsy.addNote(static_cast<size_t>(duration), lyric ? lyric : "", static_cast<size_t>(pitch), false, false,
      static_cast<sinsy::TieType>(tie), static_cast<sinsy::SlurType>(slur), static_cast<sinsy::SyllabicType>(syllabic), breath != 0) ? 1 : 0;
}

CS_API int cantoria_sinsy_rest(void* handle, int duration)
{
   Handle* h = static_cast<Handle*>(handle);
   Caught c(h->messages);
   return h->sinsy.addRest(static_cast<size_t>(duration)) ? 1 : 0;
}

// Sings the score built so far, its pitch moved by halfTones (negative: down); the number of
// samples, or -1 (the reason in the messages).
CS_API int cantoria_sinsy_synthesize(void* handle, double halfTones)
{
   Handle* h = static_cast<Handle*>(handle);
   Caught c(h->messages);
   h->samples.clear();
   std::unique_ptr<sinsy::LabelStrings> label(h->sinsy.createLabelData(false, 1, 2));
   if (!label || label->size() == 0) {
      c.say("Sinsy made no labels of the score");
      return -1;
   }
   HTS_Engine_add_half_tone(&h->engine, halfTones);
   if (HTS_Engine_synthesize_from_strings(&h->engine, const_cast<char**>(label->getData()), label->size()) != TRUE) {
      HTS_Engine_refresh(&h->engine);
      c.say("hts_engine could not sing the labels");
      return -1;
   }
   size_t n = HTS_Engine_get_nsamples(&h->engine);
   h->samples.resize(n);
   for (size_t i = 0; i < n; ++i) {
      double v = HTS_Engine_get_generated_speech(&h->engine, i) / 32768.0;   // 16-bit's range
      h->samples[i] = static_cast<float>(v > 1.0 ? 1.0 : v < -1.0 ? -1.0 : v);
   }
   HTS_Engine_refresh(&h->engine);
   return static_cast<int>(n);
}

// The samples of the last synthesis into buffer; how many.
CS_API int cantoria_sinsy_samples(void* handle, float* buffer, int count)
{
   Handle* h = static_cast<Handle*>(handle);
   int n = count < static_cast<int>(h->samples.size()) ? count : static_cast<int>(h->samples.size());
   std::memcpy(buffer, h->samples.data(), sizeof(float) * n);
   return n;
}
