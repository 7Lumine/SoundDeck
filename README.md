# SoundDeck

SoundDeck は、Discord などのボイスチャット向けに、マイク音声と MP3 クリップをミックスして流すための Windows デスクトップアプリです。

SoundDeck は Discord に直接音声を注入するアプリではありません。VB-CABLE や SteelSeries Sonar などの仮想オーディオデバイスへ音声を出力し、Discord 側ではその録音デバイスを入力として選びます。

## 主な機能

- マイク音声と MP3 クリップのミックス
- 複数 MP3 の同時再生
- クリップごとの再生、停止、シーク、音量、ループ、ピン留め、名前変更
- クリップごとのグローバルホットキー設定
- MP3 再生中だけマイク音量を下げるダッキング
- MP3のみ / ミックス音声のモニター出力
- デバイス、音量、クリップ名、ホットキー、各種設定の保存

## 必要なもの

- Windows 10 / Windows 11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- 仮想オーディオデバイス
  - [VB-CABLE](https://vb-audio.com/Cable/)
  - SteelSeries Sonar
  - VoiceMeeter など

一番シンプルなのは VB-CABLE を使う構成です。

## ダウンロードと起動

1. GitHub Releases から `SoundDeck-0.1.1-win-x64.zip` をダウンロードします。
2. zip を任意のフォルダへ展開します。
3. 展開したフォルダ内の `SoundDeck.exe` を起動します。

`SoundDeck.exe` だけを別の場所へ移動して起動しないでください。現在の配布形式では、同じフォルダ内の `SoundDeck.dll`、`SoundDeck.runtimeconfig.json`、`NAudio.*.dll` なども必要です。

## VB-CABLE での使い方

1. VB-CABLE をインストールします。
2. SoundDeck を起動します。
3. SoundDeck 側で以下を選びます。
   - `マイク`: 普段使っているマイク
   - `VC出力`: `CABLE Input`
4. Discord 側で以下を選びます。
   - 入力デバイス: `CABLE Output`
5. SoundDeck の `送出開始` を押します。
6. MP3 を追加して、Soundboard から再生します。

音声の流れは以下のようになります。

```text
マイク + MP3
  -> SoundDeck
  -> CABLE Input
  -> CABLE Output
  -> Discord / ボイスチャットアプリ
```

## Discord 側のおすすめ設定

MP3 が小さくなる、途切れる、不自然に変化する場合は、Discord 側で以下を調整してください。

- 入力感度を自動ではなく手動にする
- ノイズ抑制を弱める、または無効にする
- エコー除去を必要に応じて無効にする
- 自動ゲイン制御を必要に応じて無効にする

## トラブルシューティング

### アプリが起動しない

.NET 8 Desktop Runtime をインストールしてください。

https://dotnet.microsoft.com/download/dotnet/8.0

zip の中から直接起動せず、必ず展開してから `SoundDeck.exe` を起動してください。

### 音がプツプツする / 途切れる

SoundDeck の `設定` を開き、`VC出力` のレイテンシを上げてください。

- まずは `200 ms`
- まだ不安定なら `300 ms`

また、Windows 側の音声デバイス、VB-CABLE、SoundDeck のサンプルレートを可能な範囲で 48 kHz に揃えてください。

### 自分のPCでMP3音声を確認したい

右側の `モニター` を使ってください。

- `MP3`: MP3 だけを聞く
- `Mix`: マイク + MP3 のミックスを聞く

モニター出力とVC出力を同じデバイスにすると、二重再生やハウリングの原因になる場合があります。

### Discord に音が入らない

以下の組み合わせになっているか確認してください。

- SoundDeck の `VC出力`: `CABLE Input`
- Discord の入力デバイス: `CABLE Output`

また、SoundDeck の `送出開始` が有効になっているか確認してください。

## ファイルの保存場所

追加した MP3 は以下へコピーされます。

```text
%AppData%\SoundDeck\Clips
```

設定ファイルは以下に保存されます。

```text
%AppData%\SoundDeck\settings.json
```

診断ログは以下に保存されます。

```text
%AppData%\SoundDeck\sounddeck.log
```

## ソースからビルドする場合

開発する場合は .NET 8 SDK をインストールして、以下を実行してください。

```powershell
dotnet restore .\SoundDeck.sln
dotnet build .\SoundDeck.sln
dotnet run --project .\src\VoicePipe\SoundDeck.csproj
```

ローカルで配布用フォルダを作る場合:

```powershell
dotnet publish .\src\VoicePipe\SoundDeck.csproj -c Release -o .\artifacts\SoundDeck-win-x64
```

## 注意

- VC に音声を流す前に、相手や参加している場所のルールを確認してください。
- 現在の SoundDeck は VB-CABLE / Sonar などの外部仮想オーディオデバイスを前提にしています。
- 将来的にはより統合された出力方式を検討できますが、0.1.x 系は仮想オーディオデバイス経由の運用を前提にしています。
