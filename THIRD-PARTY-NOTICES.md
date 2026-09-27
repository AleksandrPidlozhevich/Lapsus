# Third-party notices

Lapsus itself is licensed under the [Business Source License 1.1](LICENSE). This file is the other
side of that: the components Lapsus ships *inside* it, each under its own licence, with the copyright
notice and permission notice those licences require a distribution to carry.

The list mirrors `AboutViewModel.Components`, which is what the About window shows at runtime — when
a dependency is added or dropped in `Lapsus.csproj`, both are part of the same edit.

| Component | Licence | Copyright |
|---|---|---|
| [Avalonia](https://avaloniaui.net) 12.1.2 | MIT | Copyright (c) AvaloniaUI OÜ, All Rights Reserved |
| [IBM Plex Sans](https://github.com/IBM/plex) (Sans, Sans Hebrew, Sans Arabic) | SIL Open Font License 1.1 | Copyright © 2017 IBM Corp. with Reserved Font Name "Plex" |
| [Noto Sans Georgian](https://github.com/notofonts/georgian) | SIL Open Font License 1.1 | Copyright 2022 The Noto Project Authors |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) 8.4.2 | MIT | Copyright (c) .NET Foundation and Contributors, All rights reserved |
| [Svg.Controls.Skia.Avalonia](https://github.com/wieslawsoltes/Svg.Skia) 12.0.0.17 | MIT | Copyright (c) 2020 Wiesław Šoltés |
| [Velopack](https://github.com/velopack/velopack) 1.2.0 | MIT | Copyright (c) 2021 Caelan Sayler; Copyright (c) 2024 Velopack Ltd. |
| [ONNX Runtime GenAI](https://github.com/microsoft/onnxruntime-genai) | MIT | Copyright (c) Microsoft Corporation |
| [SymSpell](https://github.com/wolfgarbe/symspell) 6.7.3 | MIT | Copyright (c) 2018 Wolf Garbe |
| [WeCantSpell.Hunspell](https://github.com/aarondandy/WeCantSpell.Hunspell) 7.0.1 | MPL 1.1 (of its MPL 1.1 / GPL 2.0 / LGPL 2.1 tri-licence) | Copyright (C) 2002-2005 Kevin Hendricks (MySpell) and Németh László (Hunspell); .NET port by Aaron Dandy and contributors |

Two notes on that table. **ONNX Runtime GenAI ships in two flavours** and the version depends on the
platform: Windows builds carry `Microsoft.ML.OnnxRuntimeGenAI.DirectML` 0.14.1, every other platform
`Microsoft.ML.OnnxRuntimeGenAI` 0.15.2, because the DirectML package lags. Both are MIT and both
carry native ONNX Runtime libraries under the same licence. **Avalonia is one row for three packages**
— `Avalonia`, `Avalonia.Desktop` and `Avalonia.Themes.Fluent` are the same project at the same version
under the same licence. The two typefaces are not packages at all: they are TTF files committed under
`Lapsus/Assets/Fonts/` (the site's own type, so the app and getlapsus.com read alike), each next to
its OFL text, and the SIL OFL below is the licence they ship under.

`AvaloniaUI.DiagnosticsSupport` is deliberately absent. It is Debug-only (`IncludeAssets=None`
outside Debug), so it is not in a shipped build. The xUnit, coverlet and test-SDK packages are absent
for the same reason — they build the tests, not the app.

## Not bundled, and why that matters

Dictionaries and neural models are **not** part of any Lapsus distribution. Nothing is included until
the person using the app asks for a specific download, and each pack keeps the licence of whoever
published it — listed per entry in `DictionaryCatalog` and `ModelCatalog`, and shown in the Settings
window before a download starts. A language's dictionary is built on the device from up to three sources,
each pinned to a commit in `DictionaryCatalog` and fetched unmodified — the one exception being the Ukrainian
frequency list, whose Russian entries are dropped on the device after download:

- **Frequency lists** — [hermitdave/FrequencyWords](https://github.com/hermitdave/FrequencyWords), built
  from OpenSubtitles 2018 (MIT for its code, CC BY-SA 4.0 for the lists), for every language but two; Belarusian
  and Georgian take [Unilex](https://github.com/unicode-org/unilex) web-text counts instead, as sorted by
  [lingua-libre/unilex-extended](https://github.com/lingua-libre/unilex-extended) (each file declares the Unicode
  Data Files licence, SPDX `Unicode-DFS-2016`; the fork's own GPL 3 `LICENSE` covers its sorting scripts, not the
  data). A language takes exactly one of the two; they are never merged.
- **Hunspell dictionaries** — every word form of the language, from
  [LibreOffice/dictionaries](https://github.com/LibreOffice/dictionaries) or
  [wooorm/dictionaries](https://github.com/wooorm/dictionaries). Only dictionaries offered under a licence that
  leaves Lapsus free to be sold are used — MIT, BSD, MPL, LGPL, Apache, CC BY, CC BY-SA, and the EKI licence for
  Estonian; each one's licence is in its catalog entry and shown beside the installed language. Where a
  language's only Hunspell dictionary is GPL (Bulgarian, Macedonian, German, Italian, Czech, Vietnamese) or none
  exists (Finnish), that language gets its frequency lists alone.
- **Word forms from Wiktionary and UniMorph** — for Hebrew, whose Hunspell dictionary (hspell) and every word list
  built from it are AGPL: the forms listed by English Wiktionary, via [kaikki.org](https://kaikki.org)'s extract
  (Wiktionary text is CC BY-SA 4.0), and by [UniMorph](https://github.com/unimorph/heb) (CC BY-SA 3.0), built on
  the device into a word list that is an adaptation of both and so is CC BY-SA 4.0 itself.

### Every dictionary, its authors and its licence

Checked against the licence and README files at the commit each source is pinned to. Where upstream offers a
choice, the column says which licence Lapsus takes it under; GPL is never the one chosen. The About window lists the
same sources, licences and authors under **Dictionaries**, read from `DictionaryCatalog`.

| Language | Frequency list | Word forms | Taken under | Authors / copyright |
|---|---|---|---|---|
| Ukrainian | FrequencyWords, Russian words taken out with LibreOffice `ru_RU` (BSD, deleted after use) | LibreOffice `uk_UA` | MPL 1.1 | Andriy Rysin and the [dict_uk](https://github.com/brown-uk/dict_uk) project |
| Belarusian | Unilex | LibreOffice `be_BY` (official 2008 orthography) | CC BY-SA 4.0 or LGPL 3 | Aleś Bułojčyk, Uładzimir Koščanka ([Belarusian Grammar Database](https://bnkorpus.info/grammar.html)) |
| Bulgarian | FrequencyWords | — (Hunspell is GPL only) | — | — |
| Macedonian | FrequencyWords | — (Hunspell is GPL 3 only) | — | — |
| Russian | FrequencyWords | LibreOffice `ru_RU` | BSD-style | © 1997–2008 Alexander I. Lebedev; modified by László Németh |
| Greek | FrequencyWords | LibreOffice `el_GR` | MPL 1.1 (of MPL 1.1 / GPL 2 / LGPL 2.1) | Steve Stavropoulos ([elspell](http://elspell.math.upatras.gr)), Evripidis Papakostas |
| Hebrew | FrequencyWords | Wiktionary via kaikki.org + UniMorph `heb` | CC BY-SA 4.0 (UniMorph CC BY-SA 3.0) | Wiktionary contributors; Omer Goldman (UniMorph annotator) |
| Arabic | FrequencyWords | LibreOffice `ar` ([Ayaspell](http://ayaspell.sourceforge.net)) | MPL 1.1 (of GPL 2 / LGPL 2.1 / MPL 1.1) | Mohamed Kebdani, Taha Zerrouki; packaged by Ahmad Farghal |
| Georgian | Unilex | wooorm `ka` ([ka_GE.spell](https://github.com/gamag/ka_GE.spell)) | MIT | gamag; built from Kevin Scannell's [Crúbadán](http://crubadan.org/languages/ka) lists (CC BY 4.0) and Dato Bumbeishvili's [GeoWordsDatabase](https://github.com/bumbeishvili/GeoWordsDatabase) (MIT) |
| Serbian (Latin) | FrequencyWords | LibreOffice `sr-Latn` | MPL 2.0 or LGPL 3 (of those and GPL 3) | Milutin Smiljanić |
| English | FrequencyWords | wooorm `en` (en_US, [SCOWL](http://wordlist.aspell.net) size 60) | SCOWL licence and BSD | © 2000–2018 Kevin Atkinson; © 1993 Geoff Kuenning (Ispell); © 2016 Benjamin Titze (VarCon) |
| German | FrequencyWords | — (Hunspell is GPL only) | — | — |
| French | FrequencyWords | wooorm `fr` ([Grammalecte](https://grammalecte.net) 7.5) | MPL 2.0 | Olivier R. and Grammalecte contributors |
| Spanish | FrequencyWords | LibreOffice `es_ES` ([RLA-ES](https://github.com/sbosio/rla-es)) | MPL 1.1 (of GPL 3 / LGPL 3 / MPL 1.1) | Santiago Bosio and contributors |
| Italian | FrequencyWords | — (Hunspell is GPL only) | — | — |
| Portuguese | FrequencyWords | LibreOffice `pt_BR` (VERO) | LGPL 3 or MPL | © 2006–2013 Raimundo Santos Moura and team |
| Dutch | FrequencyWords | LibreOffice `nl_NL` | BSD 3-clause or CC BY 3.0 | [OpenTaal](https://opentaal.org) (Simon Brouwer, Sander van Geloven, Ruud Baars); © 1996 Nederlandstalige TeX Gebruikersgroep |
| Polish | FrequencyWords | LibreOffice `pl_PL` ([sjp.pl](https://sjp.pl)) | MPL 1.1, Apache 2.0 or CC BY 4.0 (of those, GPL 2 and LGPL 2.1) | sjp.pl, Marek Futrega |
| Czech | FrequencyWords | — (Hunspell is GPL only) | — | — |
| Slovak | FrequencyWords | LibreOffice `sk_SK` ([sk-spell](https://github.com/sk-spell/hunspell-sk)) | MPL 1.1 (of GPL 2 / LGPL 2.1 / MPL 1.1) | sk-spell project, Zdenko Podobný |
| Slovenian | FrequencyWords | LibreOffice `sl_SI` | LGPL 2.1 | Amebis d.o.o., Tomaž Erjavec, Aleš Košir, Primož Peterlin; affixes Robert Ludvik |
| Croatian | FrequencyWords | LibreOffice `hr_HR` ([hr-hunspell](https://github.com/krunose/hr-hunspell)) | MPL 1.1 (of GPL 2 / LGPL 2.1 / MPL 1.1) | Krunoslav Šebetić, Mirko Kos, Boris Jurić, Denis Lacković |
| Romanian | FrequencyWords | LibreOffice `ro_RO` | MPL 1.1 (of GPL 2 / LGPL 2.1 / MPL 1.1) | © 2005–2013 Rospell Team (Lucian Constantin and others) |
| Hungarian | FrequencyWords | LibreOffice `hu_HU` ([Magyar Ispell](http://magyarispell.sf.net)) | MPL 2.0 or LGPL 3 | © László Németh, Ferenc Godó |
| Turkish | FrequencyWords | LibreOffice `tr_TR` ([hunspell-tr](https://github.com/tdd-ai/hunspell-tr)) | MPL 2.0 | Turkish Data Depository (Ali Safaya, Arda Göktoğan, Deniz Yuret, Emirhan Kurtuluş, Taner Sezer) |
| Swedish | FrequencyWords | wooorm `sv` (Den stora svenska ordlistan) | LGPL 3 | © 2003–2019 Göran Andersson |
| Danish | FrequencyWords | LibreOffice `da_DK` ([Stavekontrolden](http://www.stavekontrolden.dk)) | MPL 1.1 (of GPL 2 / LGPL 2.1 / MPL 1.1) | © 2020 Foreningen for frit tilgængelige sprogværktøjer; data from Det Danske Sprog- og Litteraturselskab |
| Finnish | FrequencyWords | — (no Hunspell dictionary exists) | — | — |
| Lithuanian | FrequencyWords | LibreOffice `lt_LT` ([ispell-lt](https://github.com/ispell-lt/ispell-lt)) | BSD 3-clause | © 2000–2020 Albertas Agejevas and contributors |
| Latvian | FrequencyWords | LibreOffice `lv_LV` | LGPL 2.1 or later | © 2002–2020 Jānis Eisaks |
| Estonian | FrequencyWords | LibreOffice `et_EE` | LGPL 2.1 and the EKI licence | Jaak Pruulmann (LGPL); word list © Institute of the Estonian Language (EKI) |
| Indonesian | FrequencyWords | LibreOffice `id_ID` ([hunspell-id](https://github.com/shuLhan/hunspell-id)) | LGPL 3 | © 2004–2022 hunspell-id authors |
| Vietnamese | FrequencyWords | — (Hunspell is GPL 2 only) | — | — |

One piece of dictionary data *does* ship inside Lapsus: `Lapsus.Core/Spelling/Data/uk.neighbour.txt`, 564 common
Russian words to leave out of the Ukrainian list. It is Lapsus's own list, chosen by comparing word counts of
FrequencyWords with the Ukrainian word counts of [OPUS](https://opus.nlpl.eu) OpenSubtitles v2024, from
<http://www.opensubtitles.org/> — P. Lison and J. Tiedemann, 2016, *OpenSubtitles2016: Extracting Large Parallel
Corpora from Movie and TV Subtitles*, LREC 2016; no text of either corpus is in it.

Every frequency list is © its upstream: FrequencyWords © 2016 Hermit Dave (lists CC BY-SA 4.0, built from the
[OPUS OpenSubtitles 2018](https://opus.nlpl.eu/OpenSubtitles2018.php) corpus), Unilex © Unicode, Inc. Hebrew's
AGPL hspell is not used at all.

What the licences ask, given that Lapsus distributes none of these files and only fetches them at the user's
request (the filtered Ukrainian list is made on the device and never leaves it):

- **Attribution** (CC BY, CC BY-SA, BSD, MIT, SCOWL, EKI) — this table, and each language's licence in the
  Settings tooltip. The index Lapsus builds on the device is never sent anywhere; if a build ever bundles a list,
  a CC BY-SA list or anything adapted from one ships under CC BY-SA 4.0, and the BSD/MIT/SCOWL notices ship beside
  it verbatim.
- **Copyleft** (MPL, LGPL) — reaches only modified copies of the files themselves, never the program reading them.
- **The EKI licence** permits any use, selling included, provided the licence stays attached to every copy; it
  *asks* (it does not require) that users tell EKI at <tarkvara@eki.ee>.
- **Two upstream inconsistencies**, recorded rather than resolved: LibreOffice's `sk_SK/LICENSE.txt` carries only the
  GPL text while the package README and upstream (now MPL 2.0) both offer the MPL; and the Georgian dictionary's
  generator is GPL 3 while the generated dictionary is MIT. In both cases the dictionary file itself is under the
  licence given above.

## MIT License

Applies to Avalonia, CommunityToolkit.Mvvm, Svg.Controls.Skia.Avalonia, Velopack, ONNX Runtime GenAI
and SymSpell, each with its own copyright notice as listed above.

```
MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Mozilla Public License 1.1

Applies to WeCantSpell.Hunspell, used unmodified as published on NuGet. Its source code is available under the
MPL 1.1 at <https://github.com/aarondandy/WeCantSpell.Hunspell>; the full licence text is at
<https://www.mozilla.org/en-US/MPL/1.1/>. The library is a port of Hunspell and carries Hunspell's notice:

```
The contents of this file are subject to the Mozilla Public License Version
1.1 (the "License"); you may not use this file except in compliance with
the License. You may obtain a copy of the License at
http://www.mozilla.org/MPL/

Software distributed under the License is distributed on an "AS IS" basis,
WITHOUT WARRANTY OF ANY KIND, either express or implied. See the License
for the specific language governing rights and limitations under the
License.

The Original Code is Hunspell, based on MySpell.

The Initial Developers of the Original Code are
Kevin Hendricks (MySpell) and Németh László (Hunspell).
Portions created by the Initial Developers are Copyright (C) 2002-2005
the Initial Developers. All Rights Reserved.
```

## SIL Open Font License, Version 1.1

Applies to IBM Plex Sans, IBM Plex Sans Hebrew and IBM Plex Sans Arabic, Copyright © 2017 IBM Corp.
with Reserved Font Name "Plex" (https://github.com/IBM/plex), and to Noto Sans Georgian, Copyright 2022
The Noto Project Authors (https://github.com/notofonts/georgian).

```
SIL OPEN FONT LICENSE Version 1.1 - 26 February 2007
-----------------------------------------------------------

PREAMBLE
The goals of the Open Font License (OFL) are to stimulate worldwide
development of collaborative font projects, to support the font creation
efforts of academic and linguistic communities, and to provide a free and
open framework in which fonts may be shared and improved in partnership
with others.

The OFL allows the licensed fonts to be used, studied, modified and
redistributed freely as long as they are not sold by themselves. The
fonts, including any derivative works, can be bundled, embedded,
redistributed and/or sold with any software provided that any reserved
names are not used by derivative works. The fonts and derivatives,
however, cannot be released under any other type of license. The
requirement for fonts to remain under this license does not apply
to any document created using the fonts or their derivatives.

DEFINITIONS
"Font Software" refers to the set of files released by the Copyright
Holder(s) under this license and clearly marked as such. This may
include source files, build scripts and documentation.

"Reserved Font Name" refers to any names specified as such after the
copyright statement(s).

"Original Version" refers to the collection of Font Software components as
distributed by the Copyright Holder(s).

"Modified Version" refers to any derivative made by adding to, deleting,
or substituting -- in part or in whole -- any of the components of the
Original Version, by changing formats or by porting the Font Software to a
new environment.

"Author" refers to any designer, engineer, programmer, technical
writer or other person who contributed to the Font Software.

PERMISSION AND CONDITIONS
Permission is hereby granted, free of charge, to any person obtaining
a copy of the Font Software, to use, study, copy, merge, embed, modify,
redistribute, and sell modified and unmodified copies of the Font
Software, subject to the following conditions:

1) Neither the Font Software nor any of its individual components,
in Original or Modified Versions, may be sold by itself.

2) Original or Modified Versions of the Font Software may be bundled,
redistributed and/or sold with any software, provided that each copy
contains the above copyright notice and this license. These can be
included either as stand-alone text files, human-readable headers or
in the appropriate machine-readable metadata fields within text or
binary files as long as those fields can be easily viewed by the user.

3) No Modified Version of the Font Software may use the Reserved Font
Name(s) unless explicit written permission is granted by the corresponding
Copyright Holder. This restriction only applies to the primary font name as
presented to the users.

4) The name(s) of the Copyright Holder(s) or the Author(s) of the Font
Software shall not be used to promote, endorse or advertise any
Modified Version, except to acknowledge the contribution(s) of the
Copyright Holder(s) and the Author(s) or with their explicit written
permission.

5) The Font Software, modified or unmodified, in part or in whole,
must be distributed entirely under this license, and must not be
distributed under any other license. The requirement for fonts to
remain under this license does not apply to any document created
using the Font Software.

TERMINATION
This license becomes null and void if any of the above conditions are
not met.

DISCLAIMER
THE FONT SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO ANY WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT
OF COPYRIGHT, PATENT, TRADEMARK, OR OTHER RIGHT. IN NO EVENT SHALL THE
COPYRIGHT HOLDER BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
INCLUDING ANY GENERAL, SPECIAL, INDIRECT, INCIDENTAL, OR CONSEQUENTIAL
DAMAGES, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
FROM, OUT OF THE USE OR INABILITY TO USE THE FONT SOFTWARE OR FROM
OTHER DEALINGS IN THE FONT SOFTWARE.
```
