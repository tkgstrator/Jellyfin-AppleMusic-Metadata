# CLAUDE.md

このリポジトリで作業するエージェント向けの指針。

## プロジェクトの目的

Apple Music のカタログから曲・アルバム・アーティストのメタデータとアートワークを
取得して Jellyfin に保存するプラグイン。**日本と US の両ストアフロントに対応**する
ことが既存プラグインとの主な差分。

利用者向けの説明は [README.md](README.md)、バックエンドの契約は
[docs/backend.md](docs/backend.md)、開発手順は [docs/development.md](docs/development.md)。

## 決定済みの設計（勝手に変えない）

1. **カタログの取得元は差し替え可能にする。** レスポンススキーマは Apple 公式で統一し、
   DTO・パース・フォールバック・アートワーク URL 生成は全方式で共有する。差分は
   「ベース URL」と「認証ヘッダの与え方」の 2 点だけに閉じ込める。
   - **WebPlay 方式（現在の既定）** — `music.apple.com` の JS バンドルから Web
     プレイヤー用トークンを取り出し、`amp-api.music.apple.com` を直接叩く。
     認証は `Authorization: Bearer <token>` と `Origin: https://music.apple.com`。
     実測は [docs/research/webplay-token.md](docs/research/webplay-token.md)。
   - **バックエンド方式（将来）** — 自前バックエンドが Developer Token を付与して
     中継する。プラグインは `.p8` も Developer Token も持たない。契約は
     [docs/backend.md](docs/backend.md)。
2. **DTO は Apple 公式スキーマに合わせる。**
   `{ data: [{ id, type, attributes: {...} }] }`。独自の平坦化 JSON を前提にしない。
3. **ストアフロントは優先順＋フォールバック。** 設定の順（既定 `jp` → `us`）で
   問い合わせ、見つからなければ次へ。言語 `l` はストアフロントに追従
   （`jp`→`ja-jp`, `us`→`en-us`）。設定で上書き可。
4. **マルチターゲット。** `net9.0` = Jellyfin 10.11 ABI、`net10.0` = Jellyfin 12.0 ABI。
   両方をビルドし、リリースは ABI ごとに別 zip。
5. **リリース成果物は `scripts/package.sh` が作る。** jprm / `build.yaml` は使わない。
   メタ情報は `scripts/meta.template.json` が単一の出所。
6. **UI 文言（設定画面）は英語。** 公開プラグインとして配布可能な形を保つ。
   README / docs / コミットメッセージ本文は日本語でよい。

これらを変える提案自体は歓迎だが、変更するときは人間に確認を取ること。

## リポジトリ構造

```
.devcontainer/            Dev Container（app + Jellyfin 12.0 + Jellyfin 10.11）
.github/workflows/        integration.yaml（commitlint/build/test/format）, release.yaml
docs/                     backend.md（バックエンド契約）, development.md（開発手順）
scripts/                  package.sh（リリース）, manifest.py（プラグインリポジトリ生成）,
                          deploy.sh（開発サーバー反映）, meta.template.json
Jellyfin.Plugin.AppleMusic/
  Plugin.cs               BasePlugin<PluginConfiguration>, IHasWebPages
  Configuration/          PluginConfiguration.cs, configPage.html（埋め込みリソース）
tests/Jellyfin.Plugin.AppleMusic.Tests/
Directory.Build.props     バージョンと、TFM → Jellyfin バージョン/ABI の対応表
jellyfin.ruleset          StyleCop / .NET アナライザの重大度設定
global.json               SDK 10.0.100+ / テストランナーは Microsoft.Testing.Platform
```

```
Jellyfin.Plugin.AppleMusic/Catalog/     API クライアント層（Jellyfin 非依存）
  Models/            Apple 公式スキーマの DTO
  ArtworkUrl.cs      {w}x{h} テンプレートの解決
  WebPlayTokenProvider.cs  バンドルからのトークン抽出・期限管理
  WebPlayTransport.cs      amp-api への HTTP
  AppleMusicCatalog.cs     ストアフロントのフォールバック
```

```
Jellyfin.Plugin.AppleMusic/ExternalIds/  ProviderKeys, 3 つの IExternalId,
                                         IExternalUrlProvider
Jellyfin.Plugin.AppleMusic/Providers/    Album/Artist/Song のメタデータ、
                                         Album/Artist の画像
Jellyfin.Plugin.AppleMusic/Tasks/        キャッシュ掃除の週次タスク
Jellyfin.Plugin.AppleMusic/Api/          設定画面から叩くキャッシュ操作 API
PluginServiceRegistrator.cs              カタログ層の DI 登録
```

**期限切れエントリは自分では消えない。** 読み出し時に無視されるだけなので、
`CacheMaintenanceTask`（週次）と設定画面のボタンが `PruneAsync` を呼ぶ。
`IScheduledTask.Key` は Jellyfin がユーザーのスケジュール設定を紐付ける識別子なので、
変えると設定が黙って失われる。

**`Catalog/` は `MediaBrowser.*` を参照しない。** サーバー上でステップ実行できない
事情があるため、ロジックはここに寄せてユニットテストで検証する。`Providers/` は
「カタログの戻り値を Jellyfin の型に詰め替えるだけ」の薄い層にとどめる。

**カタログ ID はストアフロント単位。** ID を保存するときは必ず
`ProviderKeys.Storefront` も一緒に書く。jp で見つけた ID を us に問い合わせると
別物を掴む。

**`ICatalogTransport` は生の JSON を返す。** デシリアライズは `AppleMusicCatalog`
の責務。こうしてあるのは、キャッシュがレスポンスをそのまま保存でき、
シリアライズの往復が要らないため。

**キャッシュは `CachingCatalogTransport` が担う。** 実 transport をラップするので、
検索も ID 引きも自動的に対象になる。効果は 2 つあり、片方だけでは不十分:
- **キャッシュ** — 同じ URL を二度取りに行かない
- **同時リクエストの束ね** — ライブラリスキャンはアルバム内の曲を並列処理するため、
  キャッシュが埋まる前に同一アルバムの問い合わせが同時に何本も飛ぶ。これを 1 本にまとめる。

### 数万曲でも壊れないための制約（実測値に基づく）

| 応答 | 実測サイズ |
| --- | --- |
| 検索（`limit=25`） | **約 31 KB** |
| ID 引き | 約 1.5 KB |

5 万曲なら曲の検索だけで約 1.5 GB。**.NET の文字列は UTF-16 なのでメモリ上はその倍**。
だから次の 2 つは動かさないこと。

1. **メモリ上限はバイト単位で持つ（件数ではない）。** エントリサイズが 20 倍以上
   違うため、件数で数えると実メモリが「たまたま何が入ったか」で決まってしまう。
   超過時は LRU で捨てる。
2. **大きい応答はディスクに書かない**（既定 8 KB 超）。検索応答は巨大なうえ、
   一度 ID が確定すれば Jellyfin がアイテムに保存するので以降は ID 引きになり、
   再利用されない。逆に ID 引きは小さく何度も引かれるので永続化する価値がある。

ディスクは **1 エントリ 1 ファイル**で、キーの SHA-256 先頭 2 文字でシャーディング。
全体を 1 ファイルに書くと書き込みが O(全体) になり、数万件で破綻する。

SQLite は使わない。Jellyfin はプラグインが再利用できる SQLite アセンブリを提供して
おらず、自前で参照するとプラットフォーム別のネイティブライブラリを同梱することになり、
サーバーが既に読み込んでいる SQLite と衝突する恐れもあるため。

`IExternalId` に `UrlFormatString` は無い（10.9 以降 `IExternalUrlProvider` に分離）。
リンク生成は `AppleMusicExternalUrlProvider` が担い、ストアフロントを含めた
正しい URL を作る。

## ビルド・テスト

```bash
dotnet build                          # net9.0 と net10.0 の両方
dotnet build -f net10.0               # 片方だけ
dotnet test                           # xunit v3 / Microsoft.Testing.Platform
dotnet format --verify-no-changes     # CI と同じ書式チェック
./scripts/deploy.sh [--legacy]        # 開発用 Jellyfin に反映して再起動
./scripts/package.sh [version]        # dist/ にリリース成果物
```

### 実 API に対する確認

`LiveCatalogTests` は本物の Apple Music エンドポイントを叩く。CI をネットワークに
依存させないため既定でスキップしてあるが、**Apple が Web プレイヤーのバンドル構成を
変えたことを検出できる唯一の手段**なので、トークン取得が疑わしいときとリリース前には
`Skip` を外して手動で回す。

```bash
dotnet test -f net10.0 --filter "FullyQualifiedName~LiveCatalogTests"
```

### 環境まわりの既知の事情

- **テスト実行には .NET 9 と .NET 10 の両ランタイム（ASP.NET Core 含む）が要る。**
  Jellyfin.Controller が ASP.NET Core に依存するため、`Microsoft.NETCore.App` だけ
  では `net9.0` のテストが起動しない。Dev Container と CI は両方入れてある。
- **`dotnet test` は .NET 10 SDK で VSTest が使えない。** `global.json` の
  `"test": { "runner": "Microsoft.Testing.Platform" }` で MTP に opt-in している。
  そのためテストプロジェクトは `<OutputType>Exe</OutputType>` かつ xunit v3。
  `Microsoft.NET.Test.Sdk` と `xunit.runner.visualstudio` は入れない。
- **プラグイン本体は Jellyfin 参照を `ExcludeAssets=runtime` で参照する**
  （サーバーが提供するため同梱してはいけない）。一方**テストプロジェクトは
  通常参照**する（サーバー外で実行するため実体が必要）。

## コーディング規約

- `TreatWarningsAsErrors=true`。警告を残さない。抑制は `jellyfin.ruleset` か
  `.editorconfig` で行い、`#pragma warning disable` は最後の手段。
- **public メンバーには XML ドキュメントコメントを書く**（`GenerateDocumentationFile=true`
  のため CS1591 がエラーになる）。
- **コード内のコメントと識別子は英語。** 日本語はドキュメントと会話のみ。
- `.editorconfig` は Jellyfin 公式テンプレート由来。書式は `dotnet format` に従う。
- ログは Serilog 形式の構造化ログ（`_logger.LogDebug("... {Id}", id)`）。文字列連結や
  補間で組み立てない（`CA2254` がエラー）。
- Jellyfin のバージョン差分は `#if JELLYFIN_10_11` / `#if JELLYFIN_12_0` で吸収する
  （`Jellyfin.Plugin.AppleMusic.csproj` で定義済み）。ただし差分が出る箇所は
  なるべく薄い層に閉じ込める。

## ブランチとリリース

```
feature/*  ──PR──▶  develop  ──マージ──▶  master  ──v* タグ──▶  正式リリース
              │                  │
              │                  └─ push ごとに dev プレリリースを更新
              └─ Integration（commitlint / actionlint / lint / build / test）
```

| ブランチ・操作 | 走るワークフロー | 成果物 |
| --- | --- | --- |
| feature ブランチへの push、PR | `integration.yaml` | なし（検証のみ） |
| `develop` への push（マージ） | `deployment.yaml` | `dev` タグのプレリリースを毎回置き換え |
| `v*` タグの push | `deployment.yaml` | 正式リリース |

- ワークフローは **`integration.yaml` と `deployment.yaml` の 2 つだけ**。
- `deployment.yaml` は `integration.yaml` を `workflow_call` で呼んでから公開する。
  **リリースへ至る経路はすべて integration を通る**ので、チェックを足すときは
  `integration.yaml` にだけ足せばよい。
- deployment から呼ぶときは `lint-commits: false` で commitlint を抑止する
  （マージコミットは Conventional Commits ではないため）。
- 開発版のバージョンは `Directory.Build.props` の上 3 桁 + `github.run_number`
  （例 `0.1.0.42`）。正式版はタグ `v0.2.0` を 4 桁に正規化して `0.2.0.0`。
- `dev` タグのリリースは毎回削除して作り直す（古い zip を残さないため）。
- リリース公開のあと `manifest` ジョブが `scripts/manifest.py` で**リリース一覧から**
  プラグインリポジトリ（`manifest.json`）を組み立て、GitHub Pages
  （<https://tkgstrator.github.io/Jellyfin-AppleMusic-Metadata/>）に配置する。
  Pages のソースは Actions（`gh-pages` ブランチは無い）。
  **manifest は ABI ごと・チャンネルごとに分ける**（`manifest.json`,
  `manifest-jellyfin-10.11.json`, `dev/` 配下に同名 2 つ）。まとめると 12.0
  サーバーが net9.0 を候補に入れ、安定版利用者が自動更新で dev を掴む。

## コミット

Conventional Commits（`.commitlintrc.yaml`、header は 128 文字まで）。
type は `build/ui/ci/docs/feat/fix/perf/refactor/revert/format/test/chore`。
CI の commitlint ジョブが検証する。

## やらないこと

- `.p8` 秘密鍵・Developer Token・Apple の資格情報をこのリポジトリに置かない。
  設定値としてもプラグインに持たせない（バックエンドの責務）。
- **Apple Music の Web ページを HTML スクレイピングしない。** WebPlay 方式が使うのは
  JSON API（`amp-api`）であって、DOM の XPath 依存ではない。先行実装が US 固定
  だったのは `aria-label='Albums'` のような英語 DOM に依存していたためで、
  同じ轍を踏まない。
- Jellyfin のランタイムアセンブリを配布物に含めない。
- `dist/`, `bin/`, `obj/`, `media/` の中身をコミットしない。

## 未確定・要相談

- 検索結果のスコアリング方針（表記ゆれ、全角/半角、カナ、`feat.` 表記の揺れ）。
- 曲単位でのマッチングに ISRC を使うか、名前＋アルバム＋トラック番号で照合するか。

[lyarenei/jellyfin-plugin-applemusic]: https://github.com/lyarenei/jellyfin-plugin-applemusic
