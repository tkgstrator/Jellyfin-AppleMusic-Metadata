# バックエンド仕様

このプラグインは Apple Music API を**直接は叩かない**。認証を担う自前のバックエンド
（プロキシ）を経由する。この文書はそのバックエンドが満たすべき契約を定義する。

---

## 1. なぜバックエンドを挟むのか

Apple Music API（`https://api.music.apple.com`）はすべてのリクエストに
**Developer Token** を要求する。これは Apple Developer Portal で発行した
**MusicKit 秘密鍵（`.p8` ファイル）で ES256 署名した JWT** であり、次の性質を持つ。

| 事情 | 帰結 |
| --- | --- |
| `.p8` は秘密鍵そのもので、ダウンロードは発行時の一度きり | 配布物（プラグインの dll）にも Jellyfin の設定 DB にも置きたくない |
| トークンの有効期限は最長 6 か月 | 期限切れのたびに再署名が必要。署名処理を持つ側が必要 |
| ES256 署名にはライブラリ依存が発生する | Jellyfin プラグインに暗号処理と鍵管理を持ち込みたくない |
| 1 つのトークンを複数クライアントで共有できる | 署名は 1 か所に集約するのが素直 |

よってプラグインが持つ秘密は「バックエンドの URL」と「任意の API キー」だけになり、
`.p8` はバックエンド側にのみ存在する。

## 2. Apple 側で事前に用意するもの

1. **Apple Developer Program** のメンバーシップ（有料）。
2. Certificates, Identifiers & Profiles → **Keys** → 新規キーを作成し、
   **MusicKit** を有効化する。
3. 生成された `AuthKey_<KeyID>.p8` をダウンロードする（**再ダウンロード不可**）。
4. 次の 3 つを控える。
   - **Team ID**（10 文字）— メンバーシップページに記載
   - **Key ID**（10 文字）— 作成したキーの ID
   - `.p8` ファイル本体

### Developer Token（JWT）の構造

```
header  { "alg": "ES256", "kid": "<Key ID>" }
payload { "iss": "<Team ID>", "iat": <発行時刻>, "exp": <有効期限 (iat + 最大15777000秒)> }
signature  ES256(.p8 の秘密鍵)
```

リクエストには `Authorization: Bearer <token>` として付与する。

> **カタログ検索に Music User Token は不要。** ユーザーのライブラリやプレイリストを
> 触る場合のみ追加で必要になるが、このプラグインはカタログのメタデータしか読まない。

## 3. バックエンドが実装すべきエンドポイント

**Apple 公式スキーマのパススルー**とする。バックエンドは Developer Token を付与して
`api.music.apple.com` に中継し、**レスポンスボディをそのまま返す**。整形しない。

| メソッド | パス | 用途 |
| --- | --- | --- |
| GET | `/v1/catalog/{storefront}/search` | 曲・アルバム・アーティストの検索 |
| GET | `/v1/catalog/{storefront}/songs/{id}` | 曲の ID 引き |
| GET | `/v1/catalog/{storefront}/albums/{id}` | アルバムの ID 引き |
| GET | `/v1/catalog/{storefront}/artists/{id}` | アーティストの ID 引き |

`{storefront}` は `jp` または `us`（プラグインの設定で決まる。将来他の地域も渡りうる
ので、バックエンドは値を検証せずそのまま転送してよい）。

### 転送ルール

- **クエリ文字列はすべてそのまま上流に転送する**（`term`, `types`, `limit`, `l`,
  `include`, `fields[...]`, `extend` など）。プラグイン側でクエリを組み立てる。
- **ステータスコードを保つ。** 404 は 404、429 は 429 のまま返す。プラグインは
  404 を「そのストアフロントに無い」と解釈して次のストアフロントへフォールバックする。
- **`Content-Type: application/json`** を維持する。

### 認証（バックエンド ← プラグイン）

プラグイン設定に API キーが入っている場合、リクエストに
`X-Api-Key: <key>` ヘッダが付く。バックエンドはこれを検証する。空欄なら
ヘッダは送られない（認証なしのバックエンドも許容する）。

### エラーレスポンス

上流の Apple のエラー JSON をそのまま返してよい。プラグインはステータスコードのみ
を見て判断し、ボディはログに出す。

## 4. プラグインが送るリクエストの例

曲の検索（日本ストアフロント）:

```http
GET /v1/catalog/jp/search?term=%E5%A4%9C%E3%81%AB%E9%A7%86%E3%81%91%E3%82%8B&types=songs&limit=25&l=ja-jp HTTP/1.1
Host: applemusic.example.com
X-Api-Key: ...
```

アルバムを ID で取得（関連アーティストも同時に取得）:

```http
GET /v1/catalog/jp/albums/1580677669?l=ja-jp&include=artists HTTP/1.1
```

## 5. プラグインが読むフィールド

レスポンスは `{ "data": [ { "id": ..., "type": ..., "attributes": {...} } ] }` の形。
プラグインが Jellyfin のメタデータに反映するのは次の項目。

### `songs`

| Apple のフィールド | Jellyfin 側 |
| --- | --- |
| `attributes.name` | 曲名 |
| `attributes.artistName` | アーティスト |
| `attributes.albumName` | アルバム |
| `attributes.trackNumber` | トラック番号 |
| `attributes.discNumber` | ディスク番号 |
| `attributes.releaseDate` | リリース日 / 制作年 |
| `attributes.genreNames` | ジャンル |
| `attributes.composerName` | 作曲者 |
| `attributes.isrc` | 外部 ID（照合に利用） |
| `attributes.artwork.url` | アートワーク |

### `albums`

`attributes.name` / `artistName` / `releaseDate` / `trackCount` / `recordLabel` /
`copyright` / `upc` / `genreNames` / `editorialNotes.standard` / `artwork.url`

### `artists`

`attributes.name` / `genreNames` / `editorialNotes.standard` / `artwork.url`

### アートワーク URL のテンプレート

`artwork.url` は次のようなテンプレート文字列で返る。

```
https://is1-ssl.mzstatic.com/image/thumb/.../{w}x{h}bb.{f}
```

プラグインが `{w}` `{h}` を設定値（既定 1400）で、`{f}` を `jpg` で置換して実際の
URL にする。バックエンドは置換しない。

## 6. 推奨事項（必須ではない）

- **キャッシュ。** 同じ曲を含むアルバムをスキャンすると同一クエリが連続する。
  バックエンド側で数分〜数時間キャッシュすると Apple への負荷とレイテンシが下がる。
- **レート制限。** Apple 側の制限に達した場合は 429 をそのまま返す。プラグインは
  リトライせずその回のメタデータ取得を諦める。
- **トークンの自動更新。** `exp` が近づいたら再署名する。プラグインはトークンの
  存在を知らないので、バックエンドが透過的に処理する。
