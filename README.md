# Jellyfin Apple Music メタデータプラグイン（JP/US）

Apple Music のカタログから**曲・アルバム・アーティスト**のメタデータとアートワークを
取得して Jellyfin に保存するプラグイン。日本ストアフロントと US ストアフロントの
両方に対応する。

> **状態: 開発中。** 曲・アルバム・アーティストのメタデータとアートワークの取得は
> 実装済み。実サーバーでの動作確認はこれから。進捗は
> [ロードマップ](#ロードマップ) を参照。

## 既存プラグインとの違い

[lyarenei/jellyfin-plugin-applemusic] が優れた先行実装だが、次の点を変えている。

| | 既存 | このプラグイン |
| --- | --- | --- |
| 地域 | US 固定 | **jp / us を切り替え、フォールバック可** |
| 言語 | `en-US` 固定 | ストアフロントに応じて `ja-jp` / `en-us` |
| 取得元 | Apple Music の Web ページをスクレイピング（JSON API 実装は experimental かつ Web 用トークンを流用） | **公式 API を自前バックエンド経由で利用** |
| 対象 | アルバム・アーティスト | **曲・アルバム・アーティスト** |
| Jellyfin | 10.11 系 | **10.11 系と 12.0 系の両対応** |

## 構成

```
Jellyfin サーバー
  └─ Apple Music プラグイン ──HTTPS──▶ amp-api.music.apple.com
        Web プレイヤー用トークンを         （Apple Music の内部カタログ API）
        music.apple.com から取得し、
        Origin ヘッダを添えて問い合わせ
```

Apple Music の Web プレイヤーが使うトークンをそのまま利用するため、**Apple Developer
Program への加入も API キーの設定も不要**で、インストールしてすぐ使える。取得できる
メタデータは公式 API と同等（実測は
[docs/research/webplay-token.md](docs/research/webplay-token.md)）。

ただしこのトークンは Apple が Web プレイヤーのために配っているものなので、**Apple が
サイト構成を変えると動かなくなる可能性がある**。その場合に備えて、`.p8` 秘密鍵を持つ
自前バックエンド経由に切り替えられる設計にしてある（契約は
[docs/backend.md](docs/backend.md)、実装は未完）。

## 必要なもの

- Jellyfin 10.11.x または 12.0.x

以上。追加の登録も鍵も要らない。

## インストール

### プラグインリポジトリから（推奨）

ダッシュボード → プラグイン → リポジトリ で、サーバーの Jellyfin バージョンに合った
manifest URL を追加し、カタログから **Apple Music (JP, US)** をインストールする。

| Jellyfin | manifest URL |
| --- | --- |
| 12.0.x | `https://tkgstrator.github.io/Jellyfin-AppleMusic-Metadata/manifest.json` |
| 10.11.x | `https://tkgstrator.github.io/Jellyfin-AppleMusic-Metadata/manifest-jellyfin-10.11.json` |

ABI ごとに manifest を分けているのは、1 つにまとめると 12.0 サーバーが 10.11 用の
アセンブリまで候補に入れてしまうため。

開発版を追いかける場合は `dev/` 配下の manifest を使う（`develop` にマージが入るたびに
更新される。安定版と混ぜると自動更新で開発版を掴むので別 URL にしてある）。

| Jellyfin | manifest URL |
| --- | --- |
| 12.0.x | `https://tkgstrator.github.io/Jellyfin-AppleMusic-Metadata/dev/manifest.json` |
| 10.11.x | `https://tkgstrator.github.io/Jellyfin-AppleMusic-Metadata/dev/manifest-jellyfin-10.11.json` |

### 手動で

[リリースページ][releases] の zip を Jellyfin バージョンに合わせて選ぶ。

| Jellyfin | 使う zip |
| --- | --- |
| 12.0.x | `apple-music_<version>_jellyfin-12.0.zip` |
| 10.11.x | `apple-music_<version>_jellyfin-10.11.zip` |

zip を Jellyfin のデータディレクトリの `plugins/Jellyfin.Plugin.AppleMusic_<version>/`
に展開し、サーバーを再起動する。開発版は [`dev` タグのプレリリース][dev]。

[releases]: https://github.com/tkgstrator/Jellyfin-AppleMusic-Metadata/releases
[dev]: https://github.com/tkgstrator/Jellyfin-AppleMusic-Metadata/releases/tag/dev

## 設定

ダッシュボード → プラグイン → **Apple Music (JP, US)**

| 項目 | 説明 |
| --- | --- |
| Storefront order | `jp → us` / `us → jp` / `jp のみ` / `us のみ` |
| Language override | Apple Music の `l` パラメータを固定したい場合のみ |
| Max search results | 1 クエリあたりの取得件数（既定 25） |
| Artwork size | アートワーク URL テンプレートに入れる辺の長さ（既定 1400） |
| Request timeout | リクエストのタイムアウト秒数（既定 30） |
| Minimum interval between requests | リクエスト間隔の下限 ms（既定 1000）。Apple は search を IP 単位で制限し、一度引っかかると長時間拒否し続けるため、リクエストは常に 1 本ずつ・この間隔で送る。ログに rate limiting が出るなら増やす |
| Cache catalog responses | ローカルにキャッシュして再取得を防ぐ（既定 ON、強く推奨） |
| Cache lifetime | キャッシュの有効日数（既定 30 日） |
| Remember "not found" for | 未ヒットを記憶する時間（既定 24 時間） |
| Memory budget | キャッシュのメモリ上限（既定 64 MB、LRU で退避） |
| Largest entry written to disk | これを超える応答はメモリのみ（既定 8 KB） |
| Backend base URL / API key | 将来のバックエンド方式用。現在は未使用 |
| Rename track files | ライブラリ整理で曲ファイルも `01 曲名.ext` に改名する（既定 ON） |
| Dry run | 整理タスクを「ログに書くだけ」にする（既定 ON）。画面の Apply ボタンには影響しない |

設定後、**ライブラリ設定でメタデータ/画像取得元として `Apple Music` を有効にする**
必要がある。

### ライブラリの整理（`[amid-…]` タグ）

マッチしたアルバムを次の構成に移動・改名できる。

```
ライブラリ/
└─ 米津玄師-[amid-530814268]/
   └─ IRIS OUT - Single-[amid-1837658528]/
      └─ 01 IRIS OUT.flac
```

ディレクトリ名の `[amid-<id>]` は Apple Music の ID で、**これが付いていると以後の
スキャンは検索を一切せず ID 引きだけで済む**。ID 引きはレート制限の対象外なので、
大きなライブラリでも安全に再スキャンできる。曲はアルバムのトラック一覧からトラック
番号で確定するため、曲ファイルにタグは要らない。

手順:

1. まず通常どおりスキャンしてメタデータを取る（ここだけ検索が走る）
2. ダッシュボード → プラグイン → Apple Music → **Preview moves** で移動予定を確認。
   結果はアーティストごとにまとまって出る（アルバム数・移動するアルバム数・改名する
   曲数の概要と、その下にアルバム 1 枚ずつの移動先）
3. 問題なければ **Apply moves now**。終わるとライブラリスキャンが自動で走る
4. 以後は「Organize the library by Apple Music ids」タスクをスケジュールしてもよい。
   設定の **Dry run** が ON の間はログに書くだけなので、確認してから OFF にする

注意:

- **パスが変わるので Jellyfin は移動後のファイルを新しいアイテムとして扱う。**
  再生回数・お気に入り・プレイリストの紐付けは消える。メタデータは `[amid-]` から
  即座に復元される
- 想定する構成は **ルート/アーティスト/アルバム** の 2 階層。それより深い構成
  （ルート/ジャンル/アーティスト/アルバム）はルート直下に移動される
- 移動先に既に何かあるアルバム、Apple Music ID の無いアルバムは触らない
- ファイル名に使えない文字（`/ \ : * ? " < > |`）は全角に置き換える
- アルバムディレクトリごと移動するので cover.jpg や .cue も一緒に動く。`CD1/`
  `CD2/` のようなサブディレクトリは `1-01 曲名.ext` の形に平坦化される

### シェルからの整理（`scripts/tag-library.sh`）

プラグインを更新せず、ライブラリが見えるターミナルから Apple Music の ID を
ライブラリに焼き付けるスクリプト。**`--nfo` を推奨**する。

判定はディレクトリ名の**完全一致**だけ（アーティストは検索結果の名前、アルバムは
そのアーティストのディスコグラフィの名前）。

```bash
# 必要なもの: bash 4+, curl, jq。ライブラリがマウントされている場所ならどこでも
./scripts/tag-library.sh --nfo --dry-run /music         # 計画を書くだけ（既定は dry run）
./scripts/tag-library.sh --nfo --only 米津玄師 /music    # 1 アーティストだけ試す
./scripts/tag-library.sh --nfo --apply /music           # 実行
./scripts/tag-library.sh --undo tag-library.plan.moves.<日時>.log   # 元に戻す
./scripts/tag-library.sh --nfo --quiet /music           # 進捗行と要約だけ
```

#### `--nfo`（推奨）

Jellyfin が音楽の隣に置いている `album.nfo` / `artist.nfo` に ID を書き足す。

```xml
<applemusicalbumid>1749425943</applemusicalbumid>
<applemusicstorefrontid>jp</applemusicstorefrontid>
```

この 2 要素は Jellyfin が元から読み書きする形式で、プラグインが `IExternalId` を
登録しているため自動的に往復する。**ファイルを 1 つも動かさない**ので、再生回数・
お気に入り・プレイリストが残るのが改名方式との決定的な差。実行後はライブラリの
**メタデータを更新**すれば ID が読み戻される。

- ライブラリ設定の「アートワークとメタデータをメディアフォルダーに保存」が ON で
  あること（OFF だと Jellyfin が nfo を読まない）
- 既存の nfo は 2 要素だけ差し替える。他のフィールドには触らない
- nfo が無いディレクトリには `title` と ID だけの最小の nfo を作る。Jellyfin の
  パーサは書いてある要素しか読まないので、他の情報が消えることはない
- `--apply` の前に元の nfo を控えるので `--undo` で完全に戻せる

#### 既定（ディレクトリ改名）

**アーティストディレクトリ・アルバムディレクトリ・曲ファイル**を
`名前-[amid-id]` / `01 曲名.ext` に改名する。名前の無害化とトラック対応付けは
プラグインの整理機能と同じ規則。Jellyfin が一度も照合していないライブラリや、
nfo の保存を切っている場合でも効くが、**パスが変わるので再生回数は消える**。
`--no-tracks` で曲ファイルを除外できる。

進捗は常に出る。端末なら 1 行を書き換える形、ファイルにリダイレクトしていれば
アーティスト 1 件につき 1 行。

```
[########            ]  33%  198/597 artists  842 album(s)  1204 rename(s)  0:21:40 elapsed  ETA 0:43:12  米津玄師
```

- 検索はアーティスト 1 件につき 1 回、その先は ID 引きのみ。直列 1 秒間隔で送り、
  429 なら 30 秒 → 60 秒 → 120 秒待って諦め、そこまでの計画を書いて終わる。
  応答はディスクにキャッシュされるので、再実行は続きから進む
- 一致しなかったアーティスト・アルバムは `[skip]` としてログに出るだけで触らない
- 実行後に Jellyfin でライブラリスキャンを掛ける。移動後の項目は ID で引かれる

### アーティスト名のカバレッジ計測

ライブラリのアーティスト名を 1 件ずつ Apple Music で検索し、**名前だけで何 % を
識別できるか**を測る。「アーティスト名で先に引いてからアルバム・曲を辿る」方式に
切り替える前の下調べ用で、Jellyfin のアイテムには何も書かない。

- 設定画面の **Start a coverage run**、またはスケジュールタスク
  「Check artist coverage on Apple Music」で開始する（既定トリガーなし）
- 1 名につき検索 1 回（`limit=5`）を直列で送るので、アーティスト 2,000 件なら最短で
  30 分強かかる。**429 を受けた時点で打ち切り**、次回はキャッシュ済みの名前を飛ばして
  続きから進む
- 結果は **Show the latest report** で読む。実行中でも 25 件ごとの途中経過が見える。
  完全一致（`Exact`）と、大小文字・空白だけが違う一致（`Relaxed`）を分けて数え、
  一致しなかった名前は先頭候補と並べて表示する
- レポートは `data/apple-music/artist-coverage.json` に置く。キャッシュを消しても残る

### レート制限にかかったとき

Apple の `search` は IP 単位で制限され、`Retry-After` も残量も返さない。プラグインは
リクエストを 1 本ずつ間隔を空けて送り、429 を受けたら全体を一時停止（30 秒から倍々、
最長 5 分）して同じ問い合わせを最大 3 回まで再試行する。それでも拒否され続ける間は
待たずに諦めるので、スキャン自体は止まらないが、その間に処理された曲は未マッチのまま
残る。**制限中の未回答はキャッシュされない**ので、解除後にライブラリの
「メタデータを更新」を掛け直せば埋まる。

### キャッシュの掃除

期限切れのエントリは読み出し時に無視されるが、削除は行われない。**週次のスケジュール
タスク**「Prune the Apple Music cache」が掃除する（ダッシュボード → スケジュールされた
タスク）。設定画面からも手動で実行でき、キャッシュ全体の破棄もできる。

## 開発

```bash
# VS Code で「Reopen in Container」
dotnet build     # net9.0 (Jellyfin 10.11) と net10.0 (Jellyfin 12.0) を両方
dotnet test
./scripts/deploy.sh   # http://localhost:8096 の Jellyfin に反映
```

詳細は [docs/development.md](docs/development.md)。

## ロードマップ

- [x] 開発環境（Dev Container + 動作確認用 Jellyfin 2 台）
- [x] マルチターゲットビルド（net9.0 / net10.0）とリリース基盤
- [x] CI（lint / build / test）と develop・タグの自動リリース
- [x] プラグインリポジトリ（`manifest.json`）の GitHub Pages 公開
- [x] プラグイン本体の骨格と設定画面
- [x] カタログクライアント（ストアフロント フォールバック込み）
- [x] 外部 ID（Apple Music の曲/アルバム/アーティスト ID とストアフロント）
- [x] アルバムのメタデータ / 画像プロバイダ
- [x] アーティストのメタデータ / 画像プロバイダ
- [x] 曲のメタデータプロバイダ
- [x] ローカルキャッシュ（永続化 + 同時リクエストの束ね + 上限管理）
- [x] キャッシュの掃除（週次タスク）と手動クリア
- [x] ライブラリの整理（`[amid-…]` タグ付きディレクトリへの移動、以後は検索なしで ID 引き）
- [x] アーティスト名のカバレッジ計測（名前だけで識別できる割合の実測）
- [ ] 実サーバーでの動作確認
- [ ] 検索結果のスコアリング（表記ゆれ・全角半角・カナ）
- [ ] バックエンド方式の実装（`.p8` を持つ自前サーバー経由）

## ライセンス

GPLv3。Jellyfin のバイナリ NuGet パッケージにリンクするため、コンパイル後の
プラグインは GPLv3 になる。

## クレジット

- [jellyfin/jellyfin-plugin-template] — プラグインの雛形
- [lyarenei/jellyfin-plugin-applemusic] — 先行実装
- [qtmleap/devcontainers] — Dev Container の構成

[jellyfin/jellyfin-plugin-template]: https://github.com/jellyfin/jellyfin-plugin-template
[lyarenei/jellyfin-plugin-applemusic]: https://github.com/lyarenei/jellyfin-plugin-applemusic
[qtmleap/devcontainers]: https://github.com/qtmleap/devcontainers
