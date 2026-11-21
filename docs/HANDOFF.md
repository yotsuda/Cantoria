# Cantoria 引き継ぎ (HANDOFF)

楽譜 (音符と音節、秒で) を受け取り、歌声を返すライブラリ・CLI・VS Code 拡張。エンジンは
DiffSinger (プロセス内の ONNX)、NEUTRINO、VOICEVOX。主な呼び手は Lily#。
リポジトリは `C:\MyProj\Cantoria` (GitHub: yotsuda/Cantoria)。下のパスはリポジトリの根から。
2026-11-21 に作った (それまで引き継ぎのファイルはなかった)。

## 現在地 (2026-11-21)

- **0.5.0 を出した** (2026-11-20〜21)。GitHub Release に VSIX 3 つ、Marketplace に 3 つのターゲットとも
  listed。中身は `editors/vscode/CHANGELOG.md` の 0.5.0 節。
- **リリースの直前に直したこと**: CI が ubuntu と macOS で赤だった。`NoSoundForTests` が Windows の
  パス (`\`) を渡し、`Path.GetFileName` が Windows でしか `\` で切らないため (試験だけの誤り)。
- **0.5.0 の後 (Unreleased、`942ecba`)**: roadmap C の二つの穴を埋めた。
  - 中国語: 漢字を語で読む (长城 chang、长大 zhang、银行 hang)。`ChinesePhonemizer.Read` が音符をまたいで
    漢字の並びを最長一致で語に切る。表は `src/Cantoria.Core/DiffSinger/pinyin-phrases.txt`
    (phrase-pinyin-data の pinyin.txt から `tools/Generate-PinyinPhrases.ps1` で生成。自前の訂正の表もこのスクリプトの中)。
  - 韓国語: 終声を次の母音へ (밥이 바비、같이 가치、좋아 조아)。`KoreanPhonemizer.Carry`。
    音符をまたいでも運ぶ: hyphen があるとき、または**休符も息継ぎもなく続くとき** (`KoreanPhonemizer.SungOn`)。
    Lily# は韓国語の音節を空白で区切り hyphen を付けない (walkthrough のアリラン、site の Spring-Breeze-Bicycle) ので、
    hyphen だけでは一度も効かなかった (2026-11-21 に確かめ、ユーザーの決定で Cantoria 側で運ぶことにした)。
    運ぶ二つの音符の間では `PhrasedSinger` がフレーズを切らない (`CarriesAcross`)。
    語の境は楽譜に書かれていないので、次の音節で決める: 助詞・語尾の頭 (`KoreanPhonemizer.Endings`: 이 을 은 에 아 어 …)
    ならそのまま運ぶ。それ以外の音節は語の頭とみなし、代表音にして運ぶ (꽃 위 꼬뒤、옷 안 오단)。ㅣ・y 母音の前では運ばない
    (꽃잎이 は 꽃이피。꼰닙 の ㄴ添加は複合語ごとに違い、금요일 그묘일 には起きない)。
    語尾と同じ形で始まる語 (이름、아래) は語尾と読まれる (꽃 아래 が 꼬차래)。
  - diffsinger/Nishiren で韓国語・中国語とも描けることは確かめた (hyphen なしの 밥|이 が 바|비 と読まれることも)。
    **耳ではまだ聞いていない。** (描くたびに音が少し変わる (サンプリングが乱数) ので、wav の hash では比べられない。)
    読みだけを見るなら、Cantoria.Core を参照する小さな console で `KoreanPhonemizer.Carry` / `ChinesePhonemizer.Read` を呼ぶのが早い。

- **音符ごとの `phonemes` 上書き** (roadmap C) を入れた (2026-11-21)。`SungNote.Phonemes` (声の記号を空白区切り、
  `ko/` などの前置きは省ける)。DiffSinger だけが歌う (`DiffSingerSinger.Render` の語を作る輪で辞書と運びを飛ばす)。
  NEUTRINO・VOICEVOX は歌詞を読む。知らない記号は `NotAPhoneme` が duration を含む全モデルで調べ、
  `has no sound for "…" (note N` の言い回しで返す (Lily# がこの言葉で音符を指す)。diffsinger/Nishiren で
  발|병 (`pp y eo NG`) を描けることと、`yeo` が断られることを確かめた。**Lily# 側で書く手段はまだない**
  (Lily# のリポジトリの仕事。音節に読みを付ける仕組みに倣うのが自然)。
- **音符ごとのピッチベンド `bend`** (roadmap D、試行) を入れた (2026-11-21)。`SungNote.Bend` = (音符の頭からの秒, セント)
  の点の並び。点の間は cos でなめらか、最初の前と最後の後は保つ、音符の外は 0。`Bend.cs`、ビブラートの後・variance の前に
  f0 へ足す。phrase の鍵に `|bend1 …`。TIGER で「音符全体 −300」が 3 半音下で歌われる試験 (`DiffSingerSingerTests.ABendWrittenOnANote_IsSung`、
  声があるときだけ走る) が通った。**耳ではまだ聞いていない** (ビブラートの試行はオーナーの耳で形を直した。ベンドも聞いてもらう)。
  Lily# からはまだ送らない (`\bendAfter`・グリッサンドから作るのは Lily# の仕事)。D の残りの legato (スラー) は
  調べて見送った (理由は roadmap D: 声に glide の入力がなく、歌のスラーはほぼ melisma で Lily# が melisma として送る)。

- **roadmap F (どの機械でも歌える) を始めた** (2026-11-21、オーナーの選択)。声の規約の調査は `docs/voices-2026-11-21.md`。
  配れて歌で稼ぐのも許し、Cantoria のような推論プログラムも明記して許すのは DiffSinger の **Qixuan 绮萱** + OpenVPI の
  **pc_nsf_hifigan 2025.02** だけ。`C:\Tools\voices\Qixuan` と `C:\Tools\voices\pc_nsf_hifigan` に入れた。Cantoria は手を入れずに
  読み、ja・en・zh で歌い、`DiffSingerSingerTests` 7 件が通る。性別は作者が書いていないので表に入れていない。
  - 見つかった穴と直したこと: vocoder を持たない声は、声が名指す vocoder に関係なく設定の 1 つで歌われていた。
    `DiffSingerOptions.Vocoder()` が名指しの vocoder を探す (設定の folder・その中・その隣・声の隣)。声と vocoder を
    voices folder に並べれば設定なしで歌う。
  - 聞き比べ用の wav (scratchpad、消えうる): `qixuan-ja.wav`・`qixuan-zh.wav`・`qixuan-en.wav`。
    **オーナーの耳: どの言語もちゃんと歌えている** (2026-11-21)。
  - **F の本体を作った** (2026-11-21): `VoiceOffers` (Core。申し出・規約の要点・SHA-256 で固定した 2 つの release file。
    `.X.download` に落として `.X.unpacking` に展開し、そろってから名前を付ける＝途中で止まっても何も残らない。zip の外へ出る
    entry は断る)、CLI の `offers` / `fetch <id> [--voices-dir] [--json]`、拡張の **Download a Voice…** (`downloadVoice`:
    規約を modal で見せ「Read the Terms」で原文、同意で fetch、進み具合、止めたら残りを消す。voices folder が空なら
    `~/Cantoria/voices` にして設定する。`checkSetup` と「no voice to list」の警告の先頭ボタン)。
    CLI で GitHub から空の folder へ 15 秒、vocoder 設定なしで歌えた。**VS Code の中ではまだ動かしていない**。

**次の一手**:
1. **友人の答えを待っている** (ユーザーは韓国語も中国語も話せない)。聞き取りのページと答えの読み方は
   `docs/listening-2026-11-21.md`。答えが届いたら、そこの表で「今」が選ばれたかを見る。K1・K8〜K11 は調査の後に読みを変えた。
2. roadmap C の残り: 韓国語の語ごとに決まる変化 (발병 발뼝 の濃音化、꽃잎 꼰닙 の ㄴ添加、語尾と同じ形で始まる語)、
   表にない中国語の語 (どれも今は `phonemes` で楽譜側から直せる)。C・D はこれで区切り。次の大きな段は
   F (どの機械でも歌える: 配れる声の調査、ライセンスが先) か E (sinsy などの新しいエンジン) で、どちらもオーナーが選ぶ。
3. **オーナーの耳を待っている**: `bend` の試行 (すくい上げと落とし) の聞こえ方。
4. **F の仕上げ**: Download a Voice… を VS Code で試す (F5 の Extension Development Host、voicesFolder を空にして)。
   F の *done when* (何も入っていない機械で拡張を入れて 10 分で歌う) はそれで見る。リリースに入れるかはオーナー。
   - 終声と次の子音の音変化 (鼻音化・激音化・流音化・濃音化) は入れた (`KoreanPhonemizer.Meet`、標準発音法 12・17〜20・23 項)。
     友人の答えより前に入れたので、K9・K10 は今は「もう一方」と同じ読み。

## 最初に読むもの

- `docs/roadmap.md` — 何をどの順で、なぜ。各段に *done when*。
- `docs/design.md` — Cantoria が何か、契約 (楽譜の形)、エンジンごとの設計。
- `editors/vscode/CHANGELOG.md` — 版ごとの変更。一番上の節がリリースノートになる。
- `THIRD-PARTY-NOTICES.md` — 取り込んだデータ・コードと、その条件。データを足したらここに書く。

## セッションの終わりの義務 (ユーザー決定、2026-11-21)

各セッションの終わりに、**次は会話を続けるほうが有利か、コンテキストをクリアして新しい会話で次に着手する
ほうが有利か**を、理由を添えて報告する。判断の材料:

- 次の作業が、この会話の中にしかない知識 (未記録の調査結果、試行錯誤の経緯) に頼るか。
- 会話が長くなり、要約や古いツール出力で文脈が劣化していないか。
- docs (このファイルと上の「最初に読むもの」) が現状に追いついていて、新しいセッションがそこから始められるか。

クリアを勧めるときは、その前に、会話の中にしかない知識を docs に書き出しておく。

**判断を「クリア」に偏らせない** (Shosa で同じ義務を負ったときの教訓):

- **docs に書き出したことは、クリアする理由にならない。** 書き出しは、クリアしても失うものを減らすだけ。
  クリアする理由は、続けると損をすること (文脈の劣化、要約で失われた細部、次の作業と無関係な大量の出力) だけ。
- **続ける利点を必ず数える**: 次の作業に要るコードやファイルを、この会話でもう読んでいるか。
  ユーザーが決めることへの答えを待っていて、答えが来ればすぐ実装に入れるか。
- 会話が短く、要約がまだ起きておらず、次の作業がいまの作業の続きなら、**続けるのが既定**。
- 報告では、続ける利点とクリアの利点を両方挙げてから結論を言う。

## リリースの手順

1. 版を上げる: `editors/vscode/package.json`、`package-lock.json` (2 か所)、`src/Cantoria.Cli/Cantoria.Cli.csproj`、
   `src/Cantoria.Core/Cantoria.Core.csproj`。**`npm version` は使わない** (package.json の空白を百数十行書き換える)。
   該当行だけを置き換える。
2. CHANGELOG の `## Unreleased` を `## X.Y.Z` に。節の中に空行を入れない。
3. 試験 (下)。`tools/pack-extension.ps1 -Out <scratch>` で 3 つの VSIX が作れることを見る。
4. ユーザーが push する。**CI が 3 つの OS で緑になってから**ユーザーがタグを push する (`release.yml` が動く)。
5. **Marketplace は同じ版を二度と受け付けない。** ジョブの途中で止まったら `release.yml` を手で
   `publish_targets` 付きで動かし、残りのターゲットだけを出す。VSCE_PAT は 2027-10-16 に切れる。
6. 出せたかは `gh run view <id> --log` の `0.X.Y <target> is listed` 3 行と `gh release view vX.Y.Z` で見る。
   各ターゲットが listed になるのを待つので、全体で 30 分ほどかかる。

## 作業の決まり

- **push もタグもユーザーがする。** こちらはコミットまで。
- 試験: `dotnet test tests/Cantoria.Tests -c Release` と、`editors/vscode` で `npm test`。
- CI は Windows・Linux・macOS で走る。試験に Windows のパスを書くと Linux と macOS で落ちる (`/` で書く)。
- 声は `C:\Tools\voices` (TIGER、Nishiren、vocoder の nsf_hifigan)。NEUTRINO と VOICEVOX の場所は VS Code の
  設定 `cantoria.*` にある。CLI で描いて確かめるとき:
  `src/Cantoria.Cli/bin/Release/net10.0/cantoria.exe sing score.json --voice diffsinger/Nishiren --voices-dir C:\Tools\voices --output x.wav`
  (この Release ビルドは DirectML なしで CPU で歌う)。
- ソースに見えない文字を置かない。`ChinesePhonemizer.Toneless` の ü は分解形 (u + U+0308) で、書き直したときに
  合成形になり壊れた。今は `"u\u0308"` と書いてある。
- 生成した表 (`pinyin.txt`、`pinyin-phrases.txt`) は手で直さない。生成スクリプト (`tools/`) を直して作り直す。
- コミットの文は英語で、変わったことを文で言う (既存の履歴に合わせる)。
