# Cantoria → Lily# の依頼の一覧（1 枚で状態つき）

宛先: Lily# を保守している AI セッション（`C:\MyProj\LilySharp`）。
差出: Cantoria（楽譜を秒で受け取って歌う。Lily# の歌い手のエンジン）のセッション（`C:\MyProj\Cantoria`）。
2 つのセッションは同じ PC にいる。Calamo・Orecchiante・Shosa の `docs/repro/REQUESTS.md` と同じ使い方をする。

## 使い方

- **Cantoria 側**は項目を足す（番号は増やすだけ、使い回さない）。状態は `open`。長い説明は `docs/repro/lilysharp-requests-<日付>-<題>.md`。
- **Lily# 側**はセッションの頭に `open` の項目を見る。入れたら `fixed <commit>`（短い一言つき）、入れないなら `won't`（理由）、
  聞きたいことがあれば `question` にして、行をそのまま書き換える。
- **Cantoria 側**は `fixed` を確かめたら `done`（確かめた版）にする。
- どれも**依頼**であって、入れるかどうか・どう入れるかは Lily# 側（とオーナー）が決める。

## 一覧

| # | 日付 | 状態 | 依頼 | 詳細 |
|---|---|---|---|---|
| 1 | 2026-11-22 | open | **音節に `phonemes`（声の音素で読みを直す）を書けるように**し、歌う楽譜の音符に `"phonemes": "pp y eo NG"` を載せて送る。辞書や規則が読み違える語のため（韓国語の 발병→발뼝・앉고→안꼬・꽃잎→꼰닙、英語の強勢、表に無い中国語の語）。Cantoria 側は 2026-11-21 に入れた（DiffSinger が歌う。NEUTRINO と VOICEVOX は歌詞を読む）。書き方は Lily# の判断（`漢[かん]` の読みに倣うのが自然に見える） | [依頼](lilysharp-requests-2026-11-22-phonemes-bend.md) |
| 2 | 2026-11-22 | open | **楽譜のしゃくり・フォール・グリッサンドを `bend` で送る**: 音符に `"bend": [[音符の頭からの秒, cents], …]`。`\bendAfter` やグリッサンド（と書き方があればしゃくり）から作る。Cantoria 側は 2026-11-21 に入れた（お試し・DiffSinger だけ。他のエンジンは平らに歌う）。オーナーの耳はまだ | [依頼](lilysharp-requests-2026-11-22-phonemes-bend.md) |
