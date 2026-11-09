using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Cantoria.Audio;
using Cantoria.Voicevox;
using Xunit;

namespace Cantoria.Tests;

public class VoicevoxTests
{
    private static string Show(IEnumerable<VoicevoxNote> notes) => string.Join(' ', notes.Select(n => n.Key is null ? $"-{n.FrameLength}" : $"{n.Lyric}{n.Key}:{n.FrameLength}"));

    [Fact]
    public void AScore_BeginsAndEndsWithARest_InFrames()
    {
        var notes = VoicevoxScore.Notes([new SungNote(0, 1, 60, "か")]);
        Assert.Equal("-47 カ60:94 -47", Show(notes));
    }

    [Fact]
    public void AGap_IsARest_AndFractionsRound()
    {
        var notes = VoicevoxScore.Notes([new SungNote(0, 0.5, 60.4, "か"), new SungNote(1, 0.5, 61.6, "き")]);
        Assert.Equal("-47 カ60:47 -47 キ62:47 -47", Show(notes));
    }

    [Fact]
    public void ASyllableOfTwoMorae_SharesItsNote()
    {
        var notes = VoicevoxScore.Notes([new SungNote(0, 1, 60, "こん")]);
        Assert.Equal("-47 コ60:47 ン60:47 -47", Show(notes));
    }

    [Fact]
    public void ASmallKana_IsOneMora() => Assert.Equal("-47 キャ60:94 -47", Show(VoicevoxScore.Notes([new SungNote(0, 1, 60, "きゃ")])));

    [Fact]
    public void AMelisma_CarriesTheVowelOn()
    {
        var notes = VoicevoxScore.Notes([new SungNote(0, 0.5, 60, "さ"), new SungNote(0.5, 0.5, 62, null, Melisma: true)]);
        Assert.Equal("-47 サ60:47 ア62:47 -47", Show(notes));
    }

    [Fact]
    public void ALongVowelMark_RepeatsTheVowel()
        => Assert.Equal("-47 ト60:47 オ60:47 -47", Show(VoicevoxScore.Notes([new SungNote(0, 1, 60, "とー")])));

    [Fact]
    public void Katakana_IsReadLikeHiragana()
        => Assert.Equal("-47 ガ60:94 -47", Show(VoicevoxScore.Notes([new SungNote(0, 1, 60, "ガ")])));

    [Fact]
    public void ALatinSyllable_IsRefused_WithItsPlace()
        => Assert.Contains("frog", Assert.Throws<SingerException>(() => VoicevoxScore.Notes([new SungNote(0, 1, 60, "frog")])).Message);

    private const string Singers = """
        [{"name":"波音リツ","styles":[{"name":"ノーマル","id":6000,"type":"sing"}]},
         {"name":"ずんだもん","styles":[{"name":"ノーマル","id":3,"type":"talk"},{"name":"ノーマル","id":3003,"type":"frame_decode"},{"name":"あまあま","id":3001,"type":"frame_decode"}]}]
        """;

    [Fact]
    public void TheSingers_AreTheStylesThatSing()
    {
        var parsed = VoicevoxSingers.Parse(Singers);
        Assert.Equal(6000, parsed.Teacher);
        Assert.Equal(new[] { "波音リツ-ノーマル", "ずんだもん-ノーマル", "ずんだもん-あまあま" }, parsed.Voices.Select(v => v.Name));
        Assert.Equal(3001, parsed.Voices[2].StyleId);
    }

    private sealed class FakeEngine(byte[] wav, HttpStatusCode status = HttpStatusCode.OK, string error = "") : HttpMessageHandler
    {
        public List<(string Path, string Body)> Asked { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Asked.Add((request.RequestUri!.PathAndQuery, await request.Content!.ReadAsStringAsync(ct)));
            if (status != HttpStatusCode.OK)
            {
                return new HttpResponseMessage(status) { Content = new StringContent(error, Encoding.UTF8, "application/json") };
            }
            return request.RequestUri.AbsolutePath.EndsWith("sing_frame_audio_query")
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"f0":[1],"volume":[1],"phonemes":[],"volumeScale":1,"outputSamplingRate":24000,"outputStereo":false}""", Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(wav) };
        }
    }

    private static readonly VoicevoxVoice Zunda = new("ずんだもん-あまあま", "ずんだもん", 3001);

    [Fact]
    public async Task Singing_AsksTheTeacherForTheQuery_AndTheVoiceForTheSound_WithoutTheLead()
    {
        var fake = new FakeEngine(Wav.Write(new SungAudio(new float[48000 * 2], 48000)));
        using var singer = new VoicevoxSinger(VoicevoxEngine.Client(VoicevoxEngine.DefaultUrl, fake), Zunda, 6000);
        var audio = await singer.SingAsync(new SungScore([new SungNote(0, 1, 60, "か")], "ja", 48000));

        Assert.Equal("/sing_frame_audio_query?speaker=6000", fake.Asked[0].Path);
        var notes = JsonNode.Parse(fake.Asked[0].Body)!["notes"]!.AsArray();
        Assert.Null(notes[0]!["key"]);
        Assert.Equal(47, (int)notes[0]!["frame_length"]!);
        Assert.Equal("カ", (string)notes[1]!["lyric"]!);
        Assert.Equal("/frame_synthesis?speaker=3001", fake.Asked[1].Path);
        Assert.Equal(48000, (int)JsonNode.Parse(fake.Asked[1].Body)!["outputSamplingRate"]!);
        Assert.Equal(48000 * 2 - (int)Math.Round(47 / 93.75 * 48000), audio.Samples.Length);
        Assert.Equal("voicevox/ずんだもん-あまあま", singer.Name);
    }

    [Fact]
    public async Task EnglishWords_AreRefused()
    {
        using var singer = new VoicevoxSinger(VoicevoxEngine.Client(VoicevoxEngine.DefaultUrl, new FakeEngine([])), Zunda, 6000);
        var e = await Assert.ThrowsAsync<SingerException>(() => singer.SingAsync(new SungScore([new SungNote(0, 1, 60, "star")], "en")));
        Assert.Contains("sings Japanese", e.Message);
    }

    [Fact]
    public async Task TheEnginesRefusal_IsItsOwnSentence()
    {
        using var singer = new VoicevoxSinger(VoicevoxEngine.Client(VoicevoxEngine.DefaultUrl, new FakeEngine([], HttpStatusCode.BadRequest, """{"detail":"lyricが不正です: ヰ"}""")), Zunda, 6000);
        var e = await Assert.ThrowsAsync<SingerException>(() => singer.SingAsync(new SungScore([new SungNote(0, 1, 60, "か")])));
        Assert.Contains("lyricが不正です: ヰ", e.Message);
    }
}
