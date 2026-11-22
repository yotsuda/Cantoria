# 依頼 1・2: 音節の `phonemes` と、音符の `bend`

宛先: Lily#（`C:\MyProj\LilySharp`）。差出: Cantoria。2026-11-22。一覧は [REQUESTS.md](REQUESTS.md)。

Cantoria は 2026-11-21 に、歌う楽譜（`SungScore` の JSON）の音符へ任意の項目を 2 つ足した。
どちらも省いてよく、古い Cantoria は知らない項目を読み飛ばす。今は Cantoria を直接呼ぶ人しか使えないので、
Lily# から送れるようにしてほしい。書き方（`.lys` の構文）・どこで作るか・警告は Lily# の判断。

仕様の元は Cantoria の `README.md`（score の節）、`src/Cantoria.Core/SungScore.cs`（検査）、
`src/Cantoria.Core/ScoreJson.cs`（読み書き）、`src/Cantoria.Core/DiffSinger/Bend.cs`。

## 依頼 1: `phonemes`

### 何のため

Cantoria は歌詞（その言語の文字）から読みを作る。規則で直せない読み違いが残る:

- 韓国語: 語が持つ濃音化と ㄴ の添加（발병 발뼝、앉고 안꼬、꽃잎 꼰닙）。語と語尾の見分け（꽃 아래 を 꼬차래 と歌う。正しくは 꼬다래）。
- 英語: 辞書の強勢や分け方が歌と合わないとき。
- 中国語: 語の表（phrase-pinyin-data）に無い語の多音字。

### 契約

```json
{ "start": 1.0, "duration": 0.5, "pitch": 64, "lyric": "병", "phonemes": "pp y eo NG" }
```

- 値は**その声の**音素記号を空白で区切った文字列。声が使う言語の接頭辞（`ko/pp`）は省いてよい。
- `lyric` はそのまま残す（メッセージで音符を指すのに使う）。前後の音符はいつもどおり読む。
- **melisma の音符には付けない**（`melisma note carries no phonemes` で断る）。空白だけの値は無いのと同じ。
- 声に無い記号は、音符を指して断る。言い回しは今の `NoSoundFor` と同じ頭で、Lily# が音符を指すのに使っている
  `has no sound for "…" (note N` の部分は変えていない:
  `diffsinger/Nishiren has no sound for "병" (note 3 at 1 s): its phonemes are written "pp y eo NGG", and its duration model has no phoneme "NGG" (…)`
- 歌うのは DiffSinger の声。NEUTRINO と VOICEVOX は `phonemes` を見ずに歌詞を読む。

### 気をつけてほしいこと

- **記号は声ごと**。同じ韓国語でも声が違えば記号が違うことがある。`singer` を替えると断られうる。
  書き方を楽譜の歌詞に結びつけるか、歌い手（`singer` の行）に結びつけるかは Lily# の判断。
- 確かめた声: diffsinger/Nishiren で 발|병 に `pp y eo NG` を書いて 발뼝 と歌い、`yeo` は断られた。
- 書き方の案（決めるのは Lily#）: 読み `漢[かん]` に倣って、音節に印を付けて音素を書く。例 `병[/pp y eo NG/]`。

## 依頼 2: `bend`

### 何のため

楽譜のしゃくり上げ・フォール（`\bendAfter`）・グリッサンドを歌い手が歌う。

### 契約

```json
{ "start": 2.0, "duration": 1.0, "pitch": 67, "lyric": "あ", "bend": [[0.6, 0], [1.0, -700]] }
```

- 点の並び `[音符の頭からの秒, cents]`、時刻の順。
- 点と点の間はなめらか（cos で補間）。最初の点より前と最後の点より後はその値を保つ。音符の外では 0。
- 検査（Cantoria が断る）: 数でない点、音符の外（0〜duration 秒）の点、前の点より前の点。cents は ±2400 に丸める。
- 例: `[[0, -200], [0.12, 0]]` は下からしゃくり上げる。`[[0.6, 0], [1, -700]]` は 1 秒の音符の終わりで落ちる。
- 歌うのは DiffSinger の声（音高モデルが出した曲線に足す）。NEUTRINO と VOICEVOX は平らに歌う。
- **お試し**。TIGER で −300 cents が 3 半音下に歌われるのは測った。オーナーの耳はまだなので、形を変えるかもしれない。
  変えるときは Cantoria 側がこの一覧に書く。

### Lily# 側で作るものの案（決めるのは Lily#）

- `\bendAfter #-4` のようなフォール: 音符の後ろ寄りから下へ。量は LilyPond の値（半音）を cents に。
- グリッサンド: 前の音符の終わり寄りで次の音高へ向かう曲線（音符の外へは出せないので、前の音符の中で次の音までの差を cents に）。
  次の音符の頭は平らのままでよい。
- しゃくり: 書き方があれば、音符の頭で下から。

## 確かめ方（入れた後）

Cantoria の `cantoria sing` に Lily# が作った JSON を渡すか、Lily# の歌い手で DiffSinger の声を選んで歌う。
`phonemes` は声のログに出る音素で、`bend` は `bend: written on N notes` のログ行で、効いたかが分かる。
