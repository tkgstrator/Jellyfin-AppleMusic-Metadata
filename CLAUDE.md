# CLAUDE.md

このリポジトリで作業するエージェント向けの指針。

## プロジェクトの目的

Apple Music のカタログから曲・アルバム・アーティストのメタデータとアートワークを
取得して Jellyfin に保存するプラグイン。**日本と US の両ストアフロントに対応**する
ことが既存プラグインとの主な差分。

利用者向けの説明は [README.md](README.md)、バックエンドの契約は
[docs/backend.md](docs/backend.md)、開発手順は [docs/development.md](docs/development.md)。

## 決定済みの設計（勝手に変えない）

1. **Apple Music API は自前バックエンド経由で叩く。** プラグインは `.p8` 秘密鍵も
   Developer Token も持たない。持つのはバックエンドの URL と任意の API キーだけ。
2. **バックエンドのレスポンスは Apple 公式スキーマのパススルー。** DTO は Apple の
   `{ data: [{ id, type, attributes: {...} }] }` に合わせる。独自の平坦化 JSON を
   前提にしない。
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
scripts/                  package.sh（リリース）, deploy.sh（開発サーバー反映）, meta.template.json
Jellyfin.Plugin.AppleMusic/
  Plugin.cs               BasePlugin<PluginConfiguration>, IHasWebPages
  Configuration/          PluginConfiguration.cs, configPage.html（埋め込みリソース）
tests/Jellyfin.Plugin.AppleMusic.Tests/
Directory.Build.props     バージョンと、TFM → Jellyfin バージョン/ABI の対応表
jellyfin.ruleset          StyleCop / .NET アナライザの重大度設定
global.json               SDK 10.0.100+ / テストランナーは Microsoft.Testing.Platform
```

`Providers/`, `ExternalIds/`, `AppleMusic/`（API クライアント層）はこれから作る。
先行実装 [lyarenei/jellyfin-plugin-applemusic] の構成が参考になる。

## ビルド・テスト

```bash
dotnet build                          # net9.0 と net10.0 の両方
dotnet build -f net10.0               # 片方だけ
dotnet test                           # xunit v3 / Microsoft.Testing.Platform
dotnet format --verify-no-changes     # CI と同じ書式チェック
./scripts/deploy.sh [--legacy]        # 開発用 Jellyfin に反映して再起動
./scripts/package.sh [version]        # dist/ にリリース成果物
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

## コミット

Conventional Commits（`.commitlintrc.yaml`、header は 128 文字まで）。
type は `build/ui/ci/docs/feat/fix/perf/refactor/revert/format/test/chore`。
CI の commitlint ジョブが検証する。

## やらないこと

- `.p8` 秘密鍵・Developer Token・Apple の資格情報をこのリポジトリに置かない。
  設定値としてもプラグインに持たせない（バックエンドの責務）。
- Apple Music の Web ページのスクレイピングや、Web プレイヤー用トークンの流用を
  実装しない。公式 API とバックエンド経由に統一する。
- Jellyfin のランタイムアセンブリを配布物に含めない。
- `dist/`, `bin/`, `obj/`, `media/` の中身をコミットしない。

## 未確定・要相談

- プラグインのリポジトリ manifest（`manifest.json`）を公開するか。公開するなら
  ホスティング先とリリース CI の追加が必要。
- 検索結果のスコアリング方針（表記ゆれ、全角/半角、カナ、`feat.` 表記の揺れ）。
- 曲単位でのマッチングに ISRC を使うか、名前＋アルバム＋トラック番号で照合するか。

[lyarenei/jellyfin-plugin-applemusic]: https://github.com/lyarenei/jellyfin-plugin-applemusic
