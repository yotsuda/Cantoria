# UTAU voicebanks Cantoria could offer (research, 2026-11-28)

For a possible `utau` backend (classic concatenative banks: `oto.ini` + samples, sung by a
WORLD-based resampler as OpenUtau's worldline does), roadmap E. The question is the same as in
[voices-2026-11-21.md](voices-2026-11-21.md): which banks' terms let Cantoria's Download a Voice
show the terms and fetch the archive **from the author's own address**, and let the user sell
songs made with it. Read from the official terms on 2026-11-28 (some through Wayback snapshots or
Chrome where fetching was refused, as noted). No archive was downloaded; a few readmes were read
from inside zips by HTTP range requests.

UTAU banks have no common licence: every author writes their own, and most forbid
redistribution or ask to be contacted before commercial use. No non-Japanese bank under a real
open licence (CC BY, CC0) was found. The only curated list is oxygen-dioxide's
[voicebank-mirrors](https://github.com/oxygen-dioxide/oxygen-dioxide/blob/main/voicebank-mirrors.md)
(banks whose authors allow free redistribution; it points at its own mirrors).

A fetch from the official address is not redistribution, but no terms page read says so.
Where redistribution is forbidden (Teto), offering a fetch is a reading, not a grant.

## Candidates

| Bank | Languages / style | Fetch from the official address | Songs for money | Other programs | Credit | Files |
|---|---|---|---|---|---|---|
| **足立レイ Adachi Rei** (missile39) | ja; 連続音 (probably VCV) and 単独音 CV, ver3.5.0 | yes: "音源の再配布や改造は可能"; "プログラムや機器に組み込んでの使用も可能" (2018 text) | doujin sales, streaming, monetisation free; retail CDs and other commercial use: contact | yes (names CVC and DeepVocal ports) | not required | Dropbox shares on mechanicalgirl.jp/adachi-rei/ (`rlkey` only on that page); size not shown |
| **波音リツ Namine Ritsu** (Canon) | ja; 連続音, CVVC (eve, mabayu_uruha) | yes: art. 2 "情報の転載等の禁止 はしません" (informal) | yes: "商用・非商用を問わず…無償かつ非独占的な使用権を許諾" | yes: "他のソフトウェア上での利用はそのソフトウェアの規約に従って" | not required | direct zips under canon-voice.com/voice/ (r73_normal_ren0151.zip 265 MB, r73_strong 180 MB, r73_mabayu_uruha 129 MB, r73_eve 64 MB) |
| **重音テト Kasane Teto** (TWINDRILL) | ja CV+VCV, OpenUtau bundle; **en CVVC** | not stated; redistribution forbidden: "改変の有無を問わず…販売、配布すること" | individuals yes ("サブスク…ご自由にどうぞ", ads OK); corporate use needs a licence (via Crypton) | not forbidden; the site itself offers an OpenUtau bundle | not required | direct zips under kasaneteto.jp/assets/download/utau/ (TETO-OUset240323.zip 474.5 MB, TETO-tougou-110401.zip 90.8 MB, TETO-tandoku-100619.zip 11.3 MB, TETO-English-150401.zip 79.9 MB) |
| **筆音クミ Hitsune Kumi** (Cubialpha) | **en VCCV, zh CVVC, ko CVVC** | yes: "Allowed without permission" with the name unchanged, credit and a link; not behind a paywall | yes: "Allowed without permission" | yes: "Import and Use in Other Synthesis Software … Allowed without permission" | when redistributing: Cubialpha + link | OneDrive share links (`1drv.ms`), en 1.10 GB, zh ~765 MB, ko 318 MB; a direct fetch is untested |
| 桃音モモ Momone Momo | ja; 2009 CV, 2011 VCV | 二次配布 needs prior permission; fetch not stated | commercial free but "ご連絡だけは" | yes ("様々な用途に…自由に") | "桃音モモ" when main vocal | 2009 CV zips on momolabo.lolipop.jp (unchecked); 2011 VCV only on third-party uploaders |

Not offerable or unclear:

- **東北ずん子 project** (ずんだもん, きりたん, イタコ, めたん …, zips on zunko.jp/sozai/utau/): no UTAU
  terms; the readme defers to the character guideline, which gives individuals non-commercial use
  plus cost recovery only, commercial use to Tohoku companies. Ask SSS first, or offer with a
  non-commercial warning.
- **雪歌ユフ Yufu Sekka**: redistribution "基本的に禁止"; selling songs needs contacting the author
  first; many official links dead, author reportedly unreachable.
- **唄音ウタ / デフォ子**: shipped only inside UTAU (whose redistribution needs an application);
  use outside UTAU not addressed; the AquesTalk rights behind it unresolved.
- **Milk English** (Xepheris, ARPAsing): redistribution and porting free, but commercial use
  needs the author's approval; RAR on Wix storage.
- Xihelv 曦和律 (zh): no stable official download. Shizuma Saito: commercial use needs approval.
  Lance Lazor: paid commercial licence.

## Clauses worth keeping

- **Teto, AI**: "重音テトの音源…を使って音声合成AIなどに読み込ませることは禁止しておりません"
  (personal use), but "AIなどを使い学習させた物を配布することは禁止". A WORLD resampler is not
  training; nothing here touches Cantoria.
- **Teto, terms date**: 発行 2011-04-01, 改定 2023-04-03 (kasaneteto.jp/guideline/voice.html; Q&A
  at guideline/faq.html).
- **Ritsu**: the terms are informal (the transfer ban is crossed out in words: "別に転載してもいいわ"),
  can change without notice (art. 8), and the author may demand a work be withdrawn (art. 8(2)).
  Show them verbatim and re-read before a release.
- **Rei**: a modified bank distributed as 足立レイ must add to `log.text` and say it is modified.
  The guidelines point to the readme inside the archive, not read yet.
- **Kumi**: "By downloading … you are agreeing to abide by the listed 'Terms of Use'" fits the
  consent dialog; the terms survive the author ("may still be used and distributed"). Editing
  `oto.ini` is allowed if the edit is stated. AI training on the samples is allowed too.

## What it means

The table matters only for Download a Voice. Cantoria bundles no bank, so a `utau` backend that
reads whatever bank the user installed (as `diffsinger` does) is free of every redistribution
clause, and no terms read forbid other programs; what the user may do with the songs is between
the user and the bank's author. For a bank whose terms leave a tool's fetch unclear (Teto), the
offer can open the official page instead and let the user download it (owner, 2026-11-28).

Unlike DiffSinger, UTAU has banks whose terms fit an offer, and some sit at stable direct URLs
that `VoiceOffers` can pin by SHA-256:

- **Japanese: Namine Ritsu** first (commercial use for all, direct zips, CVVC banks), then
  **Adachi Rei** (the most permissive, but Dropbox links with a page-only key and an unread
  readme). **Teto** is the best-known voice and individuals may sell songs, but its terms forbid
  redistribution; offering it means reading "fetch from the official URL" as not distribution,
  better asked of TWINDRILL first.
- **English, Mandarin, Korean: Hitsune Kumi**, if a OneDrive share can be fetched as a file
  (the shares API can, untested). Teto English is the other English choice, under Teto's terms.
- Each bank's style (CV, VCV, CVVC, VCCV, ARPAsing) needs its own phonemizer; OpenUtau (MIT) has
  them. Kumi zh names OpenUtau's "JOAN & OSCAR" list, Kumi ko its "CVC KOR" phonemizer, Kumi en
  the VCCV English phonemizer with `envccv.yaml` (github.com/mmemim/OU-EN-VCCV-Custom-Dictionary).

Not checked: Rei's in-archive readme; whether the 2009 Momo zips still answer; the Kiritan
readme (nested zips); whether OneDrive shares fetch as files; the styles of Ritsu's 連続音 banks.
