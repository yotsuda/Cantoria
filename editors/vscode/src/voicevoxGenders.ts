// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

// VOICEVOX does not say whether a voice is male or female, so it is written down here: which of a
// score's `singer female` and `singer male` each character's voice answers to.
//
// To add or correct a voice, change one line and send a pull request. The key is the character's
// name exactly as VOICEVOX's `GET /singers` gives it. A character not listed is simply a voice that
// does not say: it can still be chosen by name, and is offered for either gender.

/** The gender of each VOICEVOX character's voice. */
export const VOICEVOX_GENDERS: Readonly<Record<string, 'female' | 'male'>> = {
    // The character's page on voicevox.hiroshiba.jp says so.
    '春日部つむぎ': 'female',
    'もち子さん': 'female',
    'WhiteCUL': 'female',
    '後鬼': 'female',
    'No.7': 'female',
    '櫻歌ミコ': 'female',
    '小夜/SAYO': 'female',
    'ナースロボ＿タイプＴ': 'female',
    '春歌ナナ': 'female',
    'あいえるたん': 'female',
    '琴詠ニア': 'female',
    '東北ずん子': 'female',
    '東北きりたん': 'female',
    '東北イタコ': 'female',
    '夜語トバリ': 'female',
    '里石ユカ': 'female',
    'ユーレイちゃん': 'female',
    '玄野武宏': 'male',
    '白上虎太郎': 'male',
    '青山龍星': 'male',
    '剣崎雌雄': 'male',
    '雀松朱司': 'male',
    '麒ヶ島宗麟': 'male',
    '栗田まろん': 'male',
    '離途': 'male',
    '黒沢冴白': 'male',

    // The page does not say: how the voice is generally taken. Corrections are welcome.
    '四国めたん': 'female',
    'ずんだもん': 'female',
    '雨晴はう': 'female',
    '波音リツ': 'female',
    '冥鳴ひまり': 'female',
    '九州そら': 'female',
    '猫使アル': 'female',
    '猫使ビィ': 'female',
    '中国うさぎ': 'female',
    'Voidoll': 'female',
    'ぞん子': 'female',
    '中部つるぎ': 'female',
    '暁記ミタマ': 'female',
    'あんこもん': 'female',
    'ブイブイ・コバコ': 'female',
    '†聖騎士 紅桜†': 'female',
    'ちび式じい': 'male',
    // Officially neither, usable as both; listed as male because few voices are.
    '満別花丸': 'male',
};

/** The gender of a VOICEVOX character's voice, or undefined when the table does not say. */
export function voicevoxGender(character: string): 'female' | 'male' | undefined {
    return Object.hasOwn(VOICEVOX_GENDERS, character) ? VOICEVOX_GENDERS[character] : undefined;
}
