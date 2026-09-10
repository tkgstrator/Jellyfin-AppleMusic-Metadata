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

リリースページの zip を、サーバーの Jellyfin バージョンに合わせて選ぶ。

| Jellyfin | 使う zip |
| --- | --- |
| 12.0.x | `apple-music_<version>_jellyfin-12.0.zip` |
| 10.11.x | `apple-music_<version>_jellyfin-10.11.zip` |

zip を Jellyfin のデータディレクトリの `plugins/Jellyfin.Plugin.AppleMusic_<version>/`
に展開し、サーバーを再起動する。

開発版が必要な場合は、[`dev` タグのプレリリース][dev] を使う。`develop` にマージが
入るたびに更新される。

[dev]: https://github.com/tkgstrator/Jellyfin-AppleMusic-Metadata/releases/tag/dev

## 設定

ダッシュボード → プラグイン → **Apple Music (JP/US)**

| 項目 | 説明 |
| --- | --- |
| Storefront order | `jp → us` / `us → jp` / `jp のみ` / `us のみ` |
| Language override | Apple Music の `l` パラメータを固定したい場合のみ |
| Max search results | 1 クエリあたりの取得件数（既定 25） |
| Artwork size | アートワーク URL テンプレートに入れる辺の長さ（既定 1400） |
| Request timeout | リクエストのタイムアウト秒数（既定 30） |
| Backend base URL / API key | 将来のバックエンド方式用。現在は未使用 |

設定後、**ライブラリ設定でメタデータ/画像取得元として `Apple Music` を有効にする**
必要がある。

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
- [x] プラグイン本体の骨格と設定画面
- [x] カタログクライアント（ストアフロント フォールバック込み）
- [x] 外部 ID（Apple Music の曲/アルバム/アーティスト ID とストアフロント）
- [x] アルバムのメタデータ / 画像プロバイダ
- [x] アーティストのメタデータ / 画像プロバイダ
- [x] 曲のメタデータプロバイダ
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
