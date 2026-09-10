# 開発ガイド

## 前提

- Docker（Apple Silicon / amd64 どちらでも可）
- VS Code + Dev Containers 拡張

ホストに .NET SDK を入れる必要はない。すべて Dev Container 内で完結する。

## Dev Container の構成

`.devcontainer/compose.yaml` は 3 つのサービスを定義する。

| サービス | 中身 | 用途 |
| --- | --- | --- |
| `app` | Ubuntu 24.04 + .NET SDK 10.0 / 9.0 | 開発コンテナ（VS Code がここに接続する） |
| `jellyfin` | `jellyfin/jellyfin:12.0.0` | 動作確認用サーバー <http://localhost:8096> |
| `jellyfin-legacy` | `jellyfin/jellyfin:10.11.11` | 旧 ABI 確認用 <http://localhost:8097>（プロファイル `legacy`） |

`app` と Jellyfin コンテナは名前付きボリュームで `/config` を共有しており、
`app` 側から直接プラグインを配置できる。

### 起動

VS Code で「Reopen in Container」を選ぶ。初回は .NET SDK の取得と
`dotnet restore` が走る。

旧 ABI 側のサーバーも使う場合:

```bash
docker compose --profile legacy up -d jellyfin-legacy
```

## 日常的な操作

```bash
# 両ターゲット（net9.0 / net10.0）をビルド
dotnet build

# テスト（xunit v3 / Microsoft.Testing.Platform）
dotnet test

# 片方のターゲットだけ
dotnet build -f net10.0
dotnet test  -f net9.0

# 書式チェック（CI と同じ）
dotnet format --verify-no-changes
```

VS Code のタスク（`Cmd+Shift+P` → Tasks: Run Task）にも同じものが登録してある。

## 動作確認サーバーへの反映

```bash
./scripts/deploy.sh            # Jellyfin 12.0 (:8096) へ
./scripts/deploy.sh --legacy   # Jellyfin 10.11 (:8097) へ
```

Debug ビルドしたうえで `$JELLYFIN_CONFIG_DIR/plugins/Jellyfin.Plugin.AppleMusic_<version>/`
に dll と pdb を置き、対象コンテナを再起動する。

反映後の確認:

1. <http://localhost:8096> にアクセス（初回はセットアップウィザード）
2. ダッシュボード → プラグイン に `Apple Music (JP/US)` が出ること
3. ダッシュボード → プラグイン → Apple Music (JP/US) で設定できること

ログ:

```bash
docker logs -f --tail 200 applemusic-jellyfin
```

プラグインのログを増やしたい場合は Jellyfin の `logging.json` で
`Jellyfin.Plugin.AppleMusic` の最小レベルを `Debug` にする。

## デバッガについて

公式の `jellyfin/jellyfin` イメージには `vsdbg` が含まれないため、コンテナ内の
Jellyfin プロセスへのステップ実行アタッチは標準では行えない。当面は次の方針とする。

- ロジックの検証は**ユニットテスト**で行う（Jellyfin に依存しない層を厚くする）
- サーバー上の挙動は**ログ**で追う

ステップ実行が必要になった時点で、Jellyfin サーバーをソースからビルドして
`app` コンテナ内で直接起動する構成（[plugin template の手順][tmpl]）を追加する。

[tmpl]: https://github.com/jellyfin/jellyfin-plugin-template#6-set-up-debugging

## テスト用の音源

`media/` はホスト・`app`・Jellyfin コンテナで共有される（`/media` にマウント）。
ここに検証用の音楽ファイルを置き、Jellyfin のライブラリとして `/media` を登録する。
`media/` の中身は git 管理外。

## リリース

```bash
./scripts/package.sh 0.2.0.0
```

`dist/` に ABI ごとの配布物が出る。

```
dist/
├── jellyfin-10.11/                              # dll + pdb + meta.json
├── jellyfin-12.0/
├── apple-music_0.2.0.0_jellyfin-10.11.zip
├── apple-music_0.2.0.0_jellyfin-10.11.zip.md5
├── apple-music_0.2.0.0_jellyfin-12.0.zip
└── apple-music_0.2.0.0_jellyfin-12.0.zip.md5
```

## CI とリリースの流れ

```
feature/*  ──PR──▶  develop  ──マージ──▶  main  ──v* タグ──▶  正式リリース
              │                  │
              │                  └─ push ごとに dev プレリリースを更新
              └─ Integration（commitlint / actionlint / lint / build / test）
```

| ワークフロー | 起動条件 | 内容 |
| --- | --- | --- |
| `integration.yaml` | feature ブランチへの push、全 PR | commitlint、actionlint、`verify` |
| `release-dev.yaml` | `develop` への push | `verify` → パッケージ → `dev` タグのプレリリースを置き換え |
| `release.yaml` | `v*` タグの push | `verify` → パッケージ → 正式リリース |
| `verify.yaml` | 上記から `workflow_call` で呼ばれる | `dotnet format` / build（net9.0・net10.0）/ test |

検証はすべて `verify.yaml` に集約してある。**リリースに至る経路は例外なくこれを通る**
ので、develop へのマージもタグリリースも、lint・ビルド・テストが通らなければ成果物は
作られない。

### 開発版の入手

`develop` にマージされるたびに `dev` タグのプレリリースが更新される。古いアーカイブを
残さないよう、毎回リリースごと作り直している。バージョンは
`Directory.Build.props` の上 3 桁 + GitHub Actions の実行番号（例 `0.1.0.42`）。

### 正式リリース

`main` で `v0.2.0` のようなタグを打って push する。バージョンは 4 桁
（`0.2.0.0`）に正規化される。リリースノートは GitHub が自動生成する。

Jellyfin のプラグインリポジトリ（`manifest.json`）を公開する場合は、この zip の
URL と MD5 を manifest に載せる。

## バージョンの決め方

`Directory.Build.props` の `<Version>` が既定値。リリース時は
`scripts/package.sh <version>` の引数、または CI がタグから導出した値で上書きされる。
Jellyfin は 4 桁のバージョンを要求するので `0.1.0.0` の形にすること。
