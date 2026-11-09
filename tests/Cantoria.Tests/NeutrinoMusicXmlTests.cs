// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Xml.Linq;
using Cantoria.Neutrino;
using Xunit;

namespace Cantoria.Tests;

public class NeutrinoMusicXmlTests
{
    private static XDocument Parse(SungScore score)
    {
        var xml = NeutrinoMusicXml.Write(score);
        // The DOCTYPE names a DTD on the web; do not fetch it.
        var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Ignore };
        using var reader = System.Xml.XmlReader.Create(new StringReader(xml), settings);
        return XDocument.Load(reader);
    }

    private static List<XElement> Measures(XDocument doc) => doc.Root!.Element("part")!.Elements("measure").ToList();

    private static int Ticks(XElement note) => (int)note.Element("duration")!;

    [Fact]
    public void Declares_Utf8_NotUtf16()
    {
        // musicXMLtoLabel read every lyric as "xx" from a file whose declaration said utf-16.
        var xml = NeutrinoMusicXml.Write(new SungScore([new SungNote(0, 1, 60, "あ")]));
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", xml);
        Assert.Contains("<text>あ</text>", xml);
    }

    [Fact]
    public void LeadIn_IsAQuarterBarOfRest_AtSixtyToTheQuarter()
    {
        var doc = Parse(new SungScore([new SungNote(0, 1, 60, "あ")]));
        var m1 = Measures(doc)[0];
        Assert.Equal("480", (string)m1.Element("attributes")!.Element("divisions")!);
        Assert.Equal("1", (string)m1.Element("attributes")!.Element("time")!.Element("beats")!);
        Assert.Equal("60", (string)m1.Descendants("per-minute").Single());
        var notes = m1.Elements("note").ToList();
        Assert.Single(notes);
        Assert.NotNull(notes[0].Element("rest"));
        Assert.Equal(480, Ticks(notes[0]));
    }

    [Fact]
    public void Bars_AreFourSeconds_AndPaddedWithRest()
    {
        // Two one-second notes: bar 2 is a + rest + b? No — a at 0, b at 2.5: a, rest, b, rest to 4 s.
        var doc = Parse(new SungScore([new SungNote(0, 1, 60, "あ"), new SungNote(2.5, 1, 62, "い")]));
        var m2 = Measures(doc)[1];
        Assert.Equal("4", (string)m2.Element("attributes")!.Element("time")!.Element("beats")!);
        var notes = m2.Elements("note").ToList();
        Assert.Equal([480, 720, 480, 240], notes.Select(Ticks));
        Assert.Null(notes[0].Element("rest"));
        Assert.NotNull(notes[1].Element("rest"));
        Assert.Null(notes[2].Element("rest"));
        Assert.NotNull(notes[3].Element("rest"));
        Assert.Equal(2, Measures(doc).Count);
    }

    [Fact]
    public void ANoteAcrossABarLine_IsSplitAndTied_LyricOnceBreathAtTheEnd()
    {
        // 3 s in, 2.5 s long: 1 s in bar 2, 1.5 s in bar 3.
        var doc = Parse(new SungScore([new SungNote(3, 2.5, 67, "ら", Breath: true)]));
        var bars = Measures(doc);
        Assert.Equal(3, bars.Count);
        var head = bars[1].Elements("note").Last();
        var tail = bars[2].Elements("note").First();
        Assert.Equal(480, Ticks(head));
        Assert.Equal(720, Ticks(tail));
        Assert.Equal("start", (string)head.Element("tie")!.Attribute("type")!);
        Assert.Equal("stop", (string)tail.Element("tie")!.Attribute("type")!);
        Assert.Equal("ら", (string)head.Element("lyric")!.Element("text")!);
        Assert.Null(tail.Element("lyric"));
        Assert.Empty(head.Descendants("breath-mark"));
        Assert.Single(tail.Descendants("breath-mark"));
    }

    [Fact]
    public void Melisma_IsTheLongVowelMark()
    {
        var doc = Parse(new SungScore([new SungNote(0, 1, 60, "ゆ"), new SungNote(1, 1, 62, Melisma: true)]));
        var texts = Measures(doc)[1].Elements("note").Select(n => (string?)n.Element("lyric")?.Element("text")).ToList();
        Assert.Equal(["ゆ", "ー", null], texts);
    }

    [Theory]
    [InlineData(60, "C", null, "4")]
    [InlineData(61, "C", "1", "4")]
    [InlineData(70, "A", "1", "4")]
    [InlineData(71, "B", null, "4")]
    [InlineData(48, "C", null, "3")]
    [InlineData(60.4, "C", null, "4")]
    public void Pitch_IsSpelledWithSharps(double midi, string step, string? alter, string octave)
    {
        var doc = Parse(new SungScore([new SungNote(0, 1, midi, "あ")]));
        var pitch = Measures(doc)[1].Elements("note").First().Element("pitch")!;
        Assert.Equal(step, (string)pitch.Element("step")!);
        Assert.Equal(alter, (string?)pitch.Element("alter"));
        Assert.Equal(octave, (string)pitch.Element("octave")!);
    }

    [Fact]
    public void AVeryShortNote_KeepsOneTick_AndTheNextStartsAfterIt()
    {
        var doc = Parse(new SungScore([new SungNote(0, 0.0005, 60, "あ"), new SungNote(0.0005, 1, 62, "い")]));
        var notes = Measures(doc)[1].Elements("note").Where(n => n.Element("rest") is null).ToList();
        Assert.Equal(1, Ticks(notes[0]));
        Assert.Equal(479, Ticks(notes[1]));   // rounded end 480, start pushed to 1
    }

    [Fact]
    public void EveryBarAfterTheLeadIn_HoldsExactlyFourSeconds()
    {
        var doc = Parse(new SungScore([
            new SungNote(0.3, 2.9, 60, "あ"), new SungNote(3.2, 5.1, 62, "い", Breath: true), new SungNote(9.0, 0.25, 64, "う"),
        ]));
        foreach (var bar in Measures(doc).Skip(1))
        {
            Assert.Equal(1920, bar.Elements("note").Sum(Ticks));
        }
    }
}
