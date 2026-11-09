// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text;
using System.Xml;

namespace Cantilena.Neutrino;

/// <summary>
/// The MusicXML NEUTRINO's <c>musicXMLtoLabel</c> reads, from a <see cref="SungScore"/>.
/// <para>
/// The score is in seconds, so the file is written at a constant 60 to the quarter with 480
/// divisions: one division is 1/480 s and nothing is lost. Bars are 4/4 (4 s); a note or rest
/// that crosses a bar line is split and tied, which NEUTRINO reads as one sound. A melisma note
/// gets the lyric <c>ー</c> (NEUTRINO's "carry the vowel on"); a breath flag becomes a
/// breath mark after the note. The first bar is a 1/4 bar holding one rest
/// (<see cref="LeadInSeconds"/>): NEUTRINO inserts a whole bar of rest itself when a score
/// starts on a note, so the lead-in is made explicit and known, and the singer trims it off
/// so the audio's time 0 is the score's.
/// </para>
/// </summary>
public static class NeutrinoMusicXml
{
    /// <summary>Divisions per quarter note.</summary>
    public const int Divisions = 480;

    /// <summary>Quarter notes per minute: one quarter is one second.</summary>
    public const int Tempo = 60;

    /// <summary>Beats (quarters) in a bar after the lead-in.</summary>
    public const int BeatsPerBar = 4;

    /// <summary>Seconds of rest before the score's time 0 (the 1/4 lead-in bar).</summary>
    public const double LeadInSeconds = 1.0;

    /// <summary>The lyric NEUTRINO reads as "continue the previous vowel".</summary>
    public const string MelismaLyric = "ー";

    private const int BarTicks = Divisions * BeatsPerBar;

    /// <summary>Ticks (divisions) for a time in seconds.</summary>
    public static int Ticks(double seconds) => (int)Math.Round(seconds * Divisions);

    /// <summary>Writes the score as an uncompressed MusicXML document (UTF-8 text).</summary>
    /// <summary>The score with each <see cref="SungNote.Inhale"/> note held to the next note's start: a
    /// breath mark with a rest after it is silence, and with none NEUTRINO sings its own breath (the
    /// <c>br</c> phoneme, its length the model's).</summary>
    public static SungScore WithInhalesClosed(SungScore score)
    {
        List<SungNote>? notes = null;
        for (int i = 0; i + 1 < score.Notes.Count; i++)
        {
            if (score.Notes[i].Inhale)
            {
                notes ??= score.Notes.ToList();
                notes[i] = notes[i] with { Duration = score.Notes[i + 1].Start - score.Notes[i].Start };
            }
        }
        return notes is null ? score : score with { Notes = notes };
    }

    public static string Write(SungScore score)
    {
        score = WithInhalesClosed(score);
        var events = Segments(score);
        // Through a stream, not a StringBuilder: a writer over text declares encoding="utf-16"
        // whatever the settings say, and musicXMLtoLabel then read every lyric as "xx".
        var utf8 = new UTF8Encoding(false);
        using var stream = new MemoryStream();
        var settings = new XmlWriterSettings { Indent = true, IndentChars = "  ", Encoding = utf8, OmitXmlDeclaration = false };
        using (var w = XmlWriter.Create(stream, settings))
        {
            w.WriteStartDocument();
            w.WriteDocType("score-partwise", "-//Recordare//DTD MusicXML 3.1 Partwise//EN", "http://www.musicxml.org/dtds/partwise.dtd", null);
            w.WriteStartElement("score-partwise");
            w.WriteAttributeString("version", "3.1");
            w.WriteStartElement("part-list");
            w.WriteStartElement("score-part");
            w.WriteAttributeString("id", "P1");
            w.WriteElementString("part-name", "voice");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteStartElement("part");
            w.WriteAttributeString("id", "P1");

            // Bar 1: the lead-in, a 1/4 bar of rest, with the attributes and the tempo.
            w.WriteStartElement("measure");
            w.WriteAttributeString("number", "1");
            w.WriteStartElement("attributes");
            w.WriteElementString("divisions", Divisions.ToString(CultureInfo.InvariantCulture));
            w.WriteStartElement("key");
            w.WriteElementString("fifths", "0");
            w.WriteEndElement();
            WriteTime(w, 1);
            w.WriteStartElement("clef");
            w.WriteElementString("sign", "G");
            w.WriteElementString("line", "2");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteStartElement("direction");
            w.WriteAttributeString("placement", "above");
            w.WriteStartElement("direction-type");
            w.WriteStartElement("metronome");
            w.WriteElementString("beat-unit", "quarter");
            w.WriteElementString("per-minute", Tempo.ToString(CultureInfo.InvariantCulture));
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteStartElement("sound");
            w.WriteAttributeString("tempo", Tempo.ToString(CultureInfo.InvariantCulture));
            w.WriteEndElement();
            w.WriteEndElement();
            WriteRest(w, Ticks(LeadInSeconds));
            w.WriteEndElement();

            // Bars 2…: 4/4, the music, each event cut at the bar lines.
            int bar = 2;
            int barStart = 0;
            bool first = true;
            foreach (var e in events)
            {
                int pos = e.Start;
                bool tiedFrom = false;
                while (pos < e.End)
                {
                    if (pos == barStart)
                    {
                        w.WriteStartElement("measure");
                        w.WriteAttributeString("number", bar.ToString(CultureInfo.InvariantCulture));
                        if (first)
                        {
                            w.WriteStartElement("attributes");
                            WriteTime(w, BeatsPerBar);
                            w.WriteEndElement();
                            first = false;
                        }
                    }
                    int barEnd = barStart + BarTicks;
                    int end = Math.Min(e.End, barEnd);
                    bool tiedTo = end < e.End;
                    if (e.Note is { } n)
                    {
                        WriteNote(w, n, end - pos, tiedFrom, tiedTo, lyric: !tiedFrom, breath: !tiedTo && n.Breath);
                    }
                    else
                    {
                        WriteRest(w, end - pos);
                    }
                    pos = end;
                    tiedFrom = tiedTo;
                    if (pos == barEnd)
                    {
                        w.WriteEndElement();   // measure
                        bar++;
                        barStart = barEnd;
                    }
                }
            }
            w.WriteEndElement();   // part
            w.WriteEndElement();   // score-partwise
            w.WriteEndDocument();
        }
        return utf8.GetString(stream.ToArray());
    }

    /// <summary>A stretch of the timeline: a note, or a rest (<see cref="Note"/> null).</summary>
    private readonly record struct Segment(int Start, int End, SungNote? Note);

    /// <summary>
    /// The score as a gapless list of notes and rests in ticks, padded with rest to a bar line.
    /// Rounding can make a short note 0 ticks or overlap its neighbour: a note keeps at least
    /// one tick and a later note starts no earlier than the one before ends.
    /// </summary>
    private static List<Segment> Segments(SungScore score)
    {
        var segments = new List<Segment>();
        int pos = 0;
        foreach (var n in score.Notes)
        {
            int start = Math.Max(pos, Ticks(n.Start));
            int end = Math.Max(start + 1, Ticks(n.End));
            if (start > pos)
            {
                segments.Add(new Segment(pos, start, null));
            }
            segments.Add(new Segment(start, end, n));
            pos = end;
        }
        int padded = Math.Max(BarTicks, (pos + BarTicks - 1) / BarTicks * BarTicks);
        if (padded > pos)
        {
            segments.Add(new Segment(pos, padded, null));
        }
        return segments;
    }

    private static void WriteTime(XmlWriter w, int beats)
    {
        w.WriteStartElement("time");
        w.WriteElementString("beats", beats.ToString(CultureInfo.InvariantCulture));
        w.WriteElementString("beat-type", "4");
        w.WriteEndElement();
    }

    private static void WriteRest(XmlWriter w, int ticks)
    {
        w.WriteStartElement("note");
        w.WriteStartElement("rest");
        w.WriteEndElement();
        w.WriteElementString("duration", ticks.ToString(CultureInfo.InvariantCulture));
        w.WriteElementString("type", TypeOf(ticks));
        w.WriteEndElement();
    }

    private static void WriteNote(XmlWriter w, SungNote n, int ticks, bool tiedFrom, bool tiedTo, bool lyric, bool breath)
    {
        int midi = Math.Clamp((int)Math.Round(n.Pitch), 0, 127);
        (string step, int alter) = (midi % 12) switch
        {
            0 => ("C", 0), 1 => ("C", 1), 2 => ("D", 0), 3 => ("D", 1), 4 => ("E", 0), 5 => ("F", 0),
            6 => ("F", 1), 7 => ("G", 0), 8 => ("G", 1), 9 => ("A", 0), 10 => ("A", 1), _ => ("B", 0),
        };
        w.WriteStartElement("note");
        w.WriteStartElement("pitch");
        w.WriteElementString("step", step);
        if (alter != 0)
        {
            w.WriteElementString("alter", alter.ToString(CultureInfo.InvariantCulture));
        }
        w.WriteElementString("octave", (midi / 12 - 1).ToString(CultureInfo.InvariantCulture));
        w.WriteEndElement();
        w.WriteElementString("duration", ticks.ToString(CultureInfo.InvariantCulture));
        if (tiedFrom)
        {
            WriteTie(w, "stop");
        }
        if (tiedTo)
        {
            WriteTie(w, "start");
        }
        w.WriteElementString("type", TypeOf(ticks));
        if (tiedFrom || tiedTo || breath)
        {
            w.WriteStartElement("notations");
            if (tiedFrom)
            {
                WriteTied(w, "stop");
            }
            if (tiedTo)
            {
                WriteTied(w, "start");
            }
            if (breath)
            {
                w.WriteStartElement("articulations");
                w.WriteStartElement("breath-mark");
                w.WriteEndElement();
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }
        if (lyric)
        {
            w.WriteStartElement("lyric");
            w.WriteAttributeString("number", "1");
            w.WriteElementString("syllabic", "single");
            w.WriteElementString("text", n.Melisma ? MelismaLyric : n.Lyric ?? "");
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }

    private static void WriteTie(XmlWriter w, string type)
    {
        w.WriteStartElement("tie");
        w.WriteAttributeString("type", type);
        w.WriteEndElement();
    }

    private static void WriteTied(XmlWriter w, string type)
    {
        w.WriteStartElement("tied");
        w.WriteAttributeString("type", type);
        w.WriteEndElement();
    }

    /// <summary>The largest note type that fits the ticks; NEUTRINO reads the duration, this is for readers.</summary>
    private static string TypeOf(int ticks) => ticks switch
    {
        >= Divisions * 4 => "whole",
        >= Divisions * 2 => "half",
        >= Divisions => "quarter",
        >= Divisions / 2 => "eighth",
        >= Divisions / 4 => "16th",
        >= Divisions / 8 => "32nd",
        _ => "64th",
    };
}
