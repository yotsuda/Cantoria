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
    運ぶ二つの音符の間では `PhrasedSinger` がフレーズを切らない (`CarriesAcross`)。語の境は分からないので、
    語をまたいでも語の中と同じに運ぶ (꽃 위 は 꼬뒤 でなく 꼬취)。
  - diffsinger/Nishiren で韓国語・中国語とも描けることは確かめた (hyphen なしの 밥|이 が 바|비 と読まれることも)。
    **耳ではまだ聞いていない。** (描くたびに音が少し変わる (サンプリングが乱数) ので、wav の hash では比べられない。)

**次の一手**:
1. ユーザーに描いた音を聞いてもらう (中国語の多音字、韓国語の連音)。Spring-Breeze-Bicycle の 햇살이、바람이 がよい例。
2. roadmap C の残り: 韓国語のほかの音変化 (鼻音化 밥만 밤만、激音化 축하 추카、濃音化)、語の境をまたぐときの終声
   (꽃 위 꼬뒤)、表にない中国語の語。ほかに roadmap C の「音符ごとの `phonemes` 上書き」、D (表情のヒント)。

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
