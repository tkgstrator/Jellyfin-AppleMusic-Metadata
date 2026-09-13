# 埋め込み Audio メタデータの優先

`EmbeddedAudioMetadataProvider` は `ICustomMetadataProvider<Audio>` として、
Jellyfin のリモートメタデータのマージ後に動く。`IPreRefreshProvider` と
`IForcedProvider` は実装しない。タグ由来の有効値は欠落補完だけでなく既存の誤値も
置換する。Apple Music の曲照合・Provider ID・画像・紹介文は変更しない。

## 読み取り

サーバーの `IMediaEncoder.ProbePath` が示す ffprobe を引数配列で起動し、FORMAT と
音声 STREAM のタグ JSON をメモリで読む。音源の変更・一時ファイル・追加バイナリは
不要。singleton の読取アダプターで最大2プロセス、1回30秒、各出力パイプ4 Mi文字を
上限とする。stdout と stderr は並行して読み、キャンセル・出力超過・失敗時も
子プロセスを終了して回収する。virtual、shortcut、非ファイルソースは対象外。

`IMediaEncoder.GetMediaInfo()` は albumartist 欠落時の補完値も返すため使用しない。
生タグを読むことで「明示された値だけ適用する」を維持する。

## 適用規則

- Artists、AlbumArtists、Genres は空でないタグ由来リストで置換し、既存値と和集合にしない。
- AlbumArtists を Artists から補完しない。Album は空白でないときだけ置換する。
- Track/Disc は正の整数、Year は検証済みの年だけ使う。無効・欠落は現在値を保持する。
- タグ名の大小文字・既知の別名を吸収する。FORMAT/STREAM や別名に矛盾があれば、
  そのフィールドを適用せず構造化ログへ残す。
- 複数値はJSON配列・同名キー・NUL境界を扱い、ライブラリで有効にされたカスタム区切りと
  whitelist、非標準 ARTISTS/ALBUMARTISTS 優先を尊重する。通常のカンマ・スラッシュ・
  `&` などから独自に複数アーティストを推測しない。
- 比較はNFCで行い、表示表記は保持する。NFKCで表示名を置き換えない。
- IsLocked は全体を、Cast は Artists/AlbumArtists を、Genres はジャンルを保護する。
- 変更したときだけ MetadataImport を返す。DBやItemValuesの保存はJellyfinに委ねる。

## 検証と限界

タグ解析にはテスト内の JSON フィクスチャを採用した。音源をコミットせず、ffprobe の
インストールや外部APIに依存せず両TFMでタグの意味と存在を検証するため。
プロセス管理はPOSIXシェルの子プロセスでも検証する（非POSIX環境のみ条件付きスキップ）。
PID確認用の一時マーカーはテスト終了時に削除する。LiveCatalogTestsは変更しない。

ユニットテストは誤マッチ後/未マッチ後のAudioへの適用、ロック、キャンセル、冪等性、
タグの矛盾、区切り設定、DI、実プロセスの回収を対象とする。
実サーバーのマージからDB保存・ItemValues更新までを通す試験はこのテストセットには
含まない。配備後は1アルバムで更新し、Audioとアーティスト/ジャンル索引の結果を確認する。
またffprobeが複数値を区別できない文字列に平坦化した場合、元の境界は復元できない。
その場合も未承認の区切りを推測して人物名を分断しない。

修復はメタデータ更新時に行う。導入だけで既存データを一括更新しない。
ロックされた項目、有効タグがない項目、読み取れなかった項目は修復されない。
