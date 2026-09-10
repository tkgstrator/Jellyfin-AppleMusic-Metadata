# Jellyfin Apple Music メタデータプラグイン（JP/US）

Apple Music のカタログから**曲・アルバム・アーティスト**のメタデータとアートワークを
取得して Jellyfin に保存するプラグイン。日本ストアフロントと US ストアフロントの
両方に対応する。

> **状態: 開発初期。** 現時点で入っているのは開発環境・ビルド/リリース基盤・
> プラグインの骨格（設定画面まで）で、メタデータ取得ロジックは未実装。
> 進捗は [ロードマップ](#ロードマップ) を参照。

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
  └─ Apple Music プラグイン ──HTTPS──▶ 自前バックエンド ──HTTPS──▶ api.music.apple.com
        設定: バックエンド URL          Developer Token (JWT/ES256)
             API キー                   を付与して中継
             ストアフロント優先順        .p8 秘密鍵はここにだけ置く
```

Apple Music API は `.p8` 秘密鍵で署名した Developer Token を必須とする。この秘密鍵を
Jellyfin 側に置かずに済ませるため、署名と中継を担うバックエンドを別に用意し、
プラグインはそこだけを見る。バックエンドは Apple のレスポンスを**そのまま**返す
（整形しない）。契約の詳細は [docs/backend.md](docs/backend.md)。

**バックエンドは本リポジトリには含まれない。** 別途用意する。

## 必要なもの

- Jellyfin 10.11.x または 12.0.x
- Apple Developer Program のメンバーシップ（`.p8` 鍵の発行に必要）
- Apple Music API を中継する自前バックエンド

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
| Backend base URL | 自前バックエンドのベース URL |
| Backend API key | バックエンドに送る `X-Api-Key`。不要なら空欄 |
| Storefront order | `jp → us` / `us → jp` / `jp のみ` / `us のみ` |
| Language override | Apple Music の `l` パラメータを固定したい場合のみ |
| Max search results | 1 クエリあたりの取得件数（既定 25） |
| Artwork size | アートワーク URL テンプレートに入れる辺の長さ（既定 1400） |
| Request timeout | バックエンドへのタイムアウト秒数（既定 30） |

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
- [ ] バックエンド API クライアント（ストアフロント フォールバック込み）
- [ ] 外部 ID（Apple Music の曲/アルバム/アーティスト ID）
- [ ] アルバムのメタデータ / 画像プロバイダ
- [ ] アーティストのメタデータ / 画像プロバイダ
- [ ] 曲のメタデータプロバイダ
- [ ] 検索結果のスコアリング（表記ゆれ・全角半角・カナ）

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
