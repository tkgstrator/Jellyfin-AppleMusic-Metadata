# 調査: Apple Music Web プレイヤートークン（2026-09-10 時点）

`music.apple.com` の JS バンドルから Web プレイヤー用トークンを取得し、Apple の内部
API（`amp-api`）を叩く方式が、実用上どこまで使えるかを実測した記録。

**この文書は調査結果であり、採用の決定ではない。**

## 実測結果

### 1. トークンの取得

現在のバンドル URL:

```
https://music.apple.com/assets/index~3eb8a0d364.js       ← モダン版（対象）
https://music.apple.com/assets/index-legacy~a5e638ce59.js ← レガシー版
```

lyarenei 版の正規表現 `/assets/index~[A-Za-z0-9]+\.js` は**現時点でそのまま通用する**
（`index-legacy~` は同パターンに一致しないため正しく除外される）。

バンドル内には JWT が 3 つ埋まっており、うち 1 つが目的のもの。

| kid | 用途 |
| --- | --- |
| `97DQU9QUD6` | 別用途 |
| `LT2ZDZSXNQ` | 別用途 |
| **`WebPlayKid`** | **Web プレイヤー用（これを使う）** |

### 2. トークンの中身

```json
{
  "iss": "AMPWebPlay",
  "iat": 1786632924,                    // 2026-08-13
  "exp": 1792680924,                    // 2026-10-22
  "root_https_origin": ["apple.com"]    // ← Origin 制約が埋め込まれている
}
```

- **有効期間は約 70 日。** 期限が来ると Apple 側でローテートされるため、
  プラグインは定期的に取り直す必要がある。
- **`root_https_origin` により Origin が `apple.com` 配下に制限されている。**

### 3. Origin ヘッダは必須

| リクエスト | 結果 |
| --- | --- |
| `Authorization` のみ | **401 Unauthorized** |
| `Authorization` + `Origin: https://music.apple.com` | **200 OK** |

`amp-api.music.apple.com` と `amp-api-edge.music.apple.com` はどちらも 200 を返す。

### 4. 取得できるデータ

`GET /v1/catalog/jp/search?term=米津玄師&types=songs&limit=2&l=ja-jp`

```
albumArtistName : 米津玄師            trackNumber  : 1
artistName      : 米津玄師            discNumber   : 1
name            : IRIS OUT           durationInMillis: 151573
albumName       : IRIS OUT - Single  releaseDate  : 2025-09-15
composerName    : 米津玄師            isrc         : JPU902502821
genreNames      : ['J-Pop', 'ミュージック']
artwork         : 4000px, テンプレート形式
hasLyrics / hasTimeSyncedLyrics / audioTraits / previews なども取得可
```

**アーティスト**（`types=artists`）:

```
name       : 米津玄師
artwork    : 2400px              ← iTunes Search API では取得できない
genreNames : ['J-Pop']
```

**アルバム**（`types=albums`）:

```
name           : YANKEE          trackCount   : 15
artistName     : 米津玄師         releaseDate  : 2014-04-23
recordLabel    : Universal Music LLC
copyright      : ℗ 2014 UNIVERSAL SIGMA, a division of UNIVERSAL MUSIC LLC
upc            : 00600406441225
editorialNotes : 「ボカロP “ハチ”として高い評価を博し…」  ← 日本語の解説文が取得可
```

### 5. ストアフロント切り替えの検証

**同一の日本語クエリ**で storefront と `l` を切り替えた結果:

| storefront | `l` | 曲名 | アーティスト | ジャンル |
| --- | --- | --- | --- | --- |
| `jp` | `ja-jp` | IRIS OUT | 米津玄師 | J-Pop, ミュージック |
| `us` | `en-us` | IRIS OUT | Kenshi Yonezu | J-Pop, Music |

**jp / us の切り替えは期待通り機能する。** 日本語クエリでも US ストアフロントは
ヒットし、表記だけが英語に変わる。

### 6. アートワーク URL

```
https://is1-ssl.mzstatic.com/image/thumb/.../{w}x{h}bb.jpg
```

想定通りのテンプレート形式。`{w}` `{h}` を置換して任意サイズを取得する。

### 7. レート制限

- **`X-Rate-Limit` 系のヘッダは返ってこない。** 残量を知る手段がない。
- `cache-control: max-age=60`、`x-cache: MISS` は返る。

公式 API（`api.music.apple.com`）は `X-Rate-Limit` ヘッダで残量を通知するが、
`amp-api` では確認できなかった。制限に当たったかどうかは 429 が返って初めて分かる。

## 評価

### 使える

必要なメタデータは**すべて取得できる**。アーティスト画像・日本語 editorial notes・
ISRC など、iTunes Search API では取れないものも含む。jp/us の切り替えも機能する。
**取得できる内容は公式 API と同等**であり、レスポンススキーマも同一。

### 残るリスク

| リスク | 実測に基づく評価 |
| --- | --- |
| バンドル構造の変更で抽出が壊れる | 現在は動く。ただし Apple の Web 更新のたびに再確認が必要 |
| Origin チェック | **実在する**（トークン payload に明記、未付与は 401）。ただしサーバー間通信なので付与は容易 |
| トークンのローテート | 約 70 日周期。自動再取得が必須 |
| レート制限 | **残量を知る手段がない**。429 が返るまで分からない |
| IP 単位の遮断 | アカウントに紐づかない以上、Apple 側の制裁手段はこれになる |

### 実装上の含意

`amp-api` と `api.music.apple.com` は**レスポンススキーマが同一**なので、
DTO・パース処理・ストアフロントのフォールバック・アートワーク URL 生成は
**両方式で完全に共有できる**。差分は次の 2 点だけに閉じ込められる。

```
        共有: DTO / パース / フォールバック / アートワーク URL 生成
          ↑
   ┌──────┴──────┐
   │  差分はここだけ  │
   ├─ ベース URL
   └─ 認証ヘッダの与え方
        ├ WebPlay 方式 : Authorization: Bearer <抽出トークン> + Origin
        └ バックエンド方式: X-Api-Key（Developer Token はバックエンドが付与）
```

したがって「まず WebPlay 方式で作り、後からバックエンド方式を足す」も、その逆も、
追加コストは小さい。
