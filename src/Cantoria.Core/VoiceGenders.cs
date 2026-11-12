// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Cantoria;

/// <summary>
/// A DiffSinger voice and a VOICEVOX character do not say whether they are male or female, so it is
/// written down: which of a score's <c>singer female</c> and <c>singer male</c> each answers to.
/// The same two tables as the extension's <c>editors/vscode/src/diffSingerGenders.ts</c> and
/// <c>voicevoxGenders.ts</c> (a test holds them to each other): to add or correct a voice, change
/// its line in both. A voice not listed is a voice that does not say.
/// </summary>
public static class VoiceGenders
{
    /// <summary>By the voice's folder name, the first part of <c>diffsinger/NAME</c>; a voice's speakers share its line.</summary>
    public static readonly IReadOnlyDictionary<string, string> DiffSinger = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Its author's or the wiki's page says so.
        ["TIGER"] = "male",

        // Not listed on purpose: Nishiren, a non-binary voice.
    };

    /// <summary>By the character's name exactly as VOICEVOX's <c>GET /singers</c> gives it.</summary>
    public static readonly IReadOnlyDictionary<string, string> Voicevox = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // The character's page on voicevox.hiroshiba.jp says so.
        ["春日部つむぎ"] = "female",
        ["もち子さん"] = "female",
        ["WhiteCUL"] = "female",
        ["後鬼"] = "female",
        ["No.7"] = "female",
        ["櫻歌ミコ"] = "female",
        ["小夜/SAYO"] = "female",
        ["ナースロボ＿タイプＴ"] = "female",
        ["春歌ナナ"] = "female",
        ["あいえるたん"] = "female",
        ["琴詠ニア"] = "female",
        ["東北ずん子"] = "female",
        ["東北きりたん"] = "female",
        ["東北イタコ"] = "female",
        ["夜語トバリ"] = "female",
        ["里石ユカ"] = "female",
        ["ユーレイちゃん"] = "female",
        ["玄野武宏"] = "male",
        ["白上虎太郎"] = "male",
        ["青山龍星"] = "male",
        ["剣崎雌雄"] = "male",
        ["雀松朱司"] = "male",
        ["麒ヶ島宗麟"] = "male",
        ["栗田まろん"] = "male",
        ["離途"] = "male",
        ["黒沢冴白"] = "male",

        // The page does not say: how the voice is generally taken. Corrections are welcome.
        ["四国めたん"] = "female",
        ["ずんだもん"] = "female",
        ["雨晴はう"] = "female",
        ["波音リツ"] = "female",
        ["冥鳴ひまり"] = "female",
        ["九州そら"] = "female",
        ["猫使アル"] = "female",
        ["猫使ビィ"] = "female",
        ["中国うさぎ"] = "female",
        ["Voidoll"] = "female",
        ["ぞん子"] = "female",
        ["中部つるぎ"] = "female",
        ["暁記ミタマ"] = "female",
        ["あんこもん"] = "female",
        ["ブイブイ・コバコ"] = "female",
        ["ちび式じい"] = "male",
        ["†聖騎士 紅桜†"] = "male",

        // Not listed on purpose: 満別花丸, officially neither.
    };
}
