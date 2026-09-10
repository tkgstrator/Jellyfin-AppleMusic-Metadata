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
| `jellyfin` | `jellyfin/jellyfin:12.0` | 動作確認用サーバー <http://localhost:8096> |
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
2. ダッシュボード → プラグイン に `Apple Music (JP, US)` が出ること
3. ダッシュボード → プラグイン → Apple Music (JP, US) で設定できること
4. 音楽ライブラリを追加してスキャンし、アルバム/曲に Apple Music の ID が付くこと
5. 設定画面の **Preview moves** で移動予定が出て、**Apply moves now** で
   `Artist-[amid-id]/Album-[amid-id]/01 Title.ext` に並ぶこと。再スキャン後のログに
   `Searching` が出ず `Looking up ... by` だけになること

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
feature/*  ──PR──▶  develop  ──マージ──▶  master  ──v* タグ──▶  正式リリース
              │                  │
              │                  └─ push ごとに dev プレリリースを更新
              └─ Integration（commitlint / actionlint / lint / build / test）
```

ワークフローは 2 つ。

| ワークフロー | 起動条件 | 内容 |
| --- | --- | --- |
| `integration.yaml` | feature ブランチへの push、全 PR、`deployment.yaml` からの呼び出し | commitlint、actionlint、`dotnet format`、build（net9.0・net10.0）、test |
| `deployment.yaml` | `develop` への push / `v*` タグの push | `integration.yaml` を呼ぶ → パッケージ → リリース公開 → manifest を GitHub Pages に公開 |

検証は `integration.yaml` に集約してある。`deployment.yaml` はそれを
`workflow_call` で呼んでから公開するので、**develop へのマージもタグリリースも、
lint・ビルド・テストが通らなければ成果物は作られない**。チェックを追加するときは
`integration.yaml` の 1 箇所だけを編集すればよい。

### 開発版の入手

`develop` にマージされるたびに `dev` タグのプレリリースが更新される。古いアーカイブを
残さないよう、毎回リリースごと作り直している。バージョンは
`Directory.Build.props` の上 3 桁 + GitHub Actions の実行番号（例 `0.1.0.42`）。

### 正式リリース

`master` で `v0.2.0` のようなタグを打って push する。バージョンは 4 桁
（`0.2.0.0`）に正規化される。リリースノートは GitHub が自動生成する。

**タグは必ず develop → master のマージコミットに打つ。** `gh pr merge` の直後に
`git pull` すると GitHub 側の反映が間に合わず 1 つ前のコミット（= develop の先頭）を
掴むことがある。`git rev-parse origin/master` が PR の mergeCommit と一致することを
確認してから `git tag` すること。develop と同じ SHA にタグを打つと、ツリーは同一で
配布物は正しいが、**GitHub Pages が同じ SHA への 2 回目以降のデプロイを反映しない**
ため、安定版 manifest が空のまま残る（v0.1.0 で実際に起きた）。

手動で走らせるときは `workflow_dispatch` に 4 桁バージョン（`0.2.0.0`）を渡す。
タグは `v0.2.0` として解決され、既存のリリースがあればそれを更新する。

### プラグインリポジトリ（manifest.json）

リリース公開のあと、`deployment.yaml` の `manifest` ジョブが
`scripts/manifest.py` で GitHub のリリース一覧から manifest を組み立て、GitHub Pages
（<https://tkgstrator.github.io/Jellyfin-AppleMusic-Metadata/>）に配置する。

```
manifest.json                     安定版, Jellyfin 12.0
manifest-jellyfin-10.11.json      安定版, Jellyfin 10.11
dev/manifest.json                 安定版 + プレリリース, Jellyfin 12.0
dev/manifest-jellyfin-10.11.json  安定版 + プレリリース, Jellyfin 10.11
```

- manifest は**この実行の成果物ではなくリリース一覧から**作る。そのため develop への
  push でもタグ push でも全ファイルを作り直すだけでよく、手で編集する箇所はない。
- ABI ごとに分けるのは、Jellyfin が `targetAbi <= サーバー` の版をすべて候補にするため。
  1 つにまとめると 12.0 サーバーに net9.0 のアセンブリが入りうる。
- 安定版と開発版を分けるのは、開発版 `0.1.0.42` が安定版 `0.1.0.0` より新しいと
  解釈され、自動更新で開発版を掴んでしまうため。
- 対象になるのは、zip と `.zip.md5` の両方が付いたリリースだけ（ドラフトは除外）。
  `.md5` が無い zip は警告を出して飛ばす。
- GitHub Pages のソースは **GitHub Actions**（`gh api -X POST repos/<owner>/<repo>/pages -f build_type=workflow` で設定済み）。ブランチ配信ではないので `gh-pages` ブランチは存在しない。

ローカルで試すには（GitHub API を読むだけなので副作用はない）:

```bash
GH_TOKEN=$(gh auth token) ./scripts/manifest.py tkgstrator/Jellyfin-AppleMusic-Metadata /tmp/site
```

## バージョンの決め方

`Directory.Build.props` の `<Version>` が既定値。リリース時は
`scripts/package.sh <version>` の引数、または CI がタグから導出した値で上書きされる。
Jellyfin は 4 桁のバージョンを要求するので `0.1.0.0` の形にすること。
