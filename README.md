# SoundDeck

SoundDeck は、Windows 上で実マイク音声と MP3 音声をミックスし、Discord などの VC アプリへ 1 つのマイク入力として渡すためのローカルツールです。

実装方針は C#/.NET + NAudio です。普段は実マイクをそのまま流し、MP3 再生中だけマイク音声に MP3 を重ねるリアルタイムミキサーを目指します。

詳細な要件は [REQUIREMENTS.md](REQUIREMENTS.md) にまとめています。

現在の UI は、デバイス、Soundboard、ミキサーを 3 カラムで並べたダークテーマです。Soundboard では複数の MP3 を同時に再生でき、各クリップを個別に再生/停止できます。

## 仕組み

1. SoundDeck が実マイク音声を受け取ります。
2. MP3 再生中は、SoundDeck が実マイク音声と 1 つ以上の MP3 音声をミックスします。
3. SoundDeck がミックス結果を `CABLE Input` などの仮想「再生」デバイスへ流します。
4. 仮想ケーブルがその音を `CABLE Output` などの仮想「録音」デバイスとして見せます。
5. Discord の入力デバイスを `CABLE Output` にすると、普段の声と MP3 が VC へ流れます。

SoundDeck は Discord へ直接音声を注入しません。Windows の音声デバイス経由で流します。

## セットアップ

.NET 8 SDK 以降を想定しています。

```powershell
dotnet restore .\SoundDeck.sln
dotnet build .\SoundDeck.sln
```

VC に流すには、あらかじめ仮想オーディオデバイスを入れてください。

- VB-CABLE
- VoiceMeeter
- SteelSeries Sonar
- OBS の仮想オーディオ系構成

一番シンプルなのは VB-CABLE です。

## 起動

```powershell
dotnet run --project .\src\VoicePipe\SoundDeck.csproj
```

## Discord への流し方

1. SoundDeck を起動します。
2. `Microphone` で普段使っているマイクを選びます。
3. `VC Output` で `CABLE Input` などの仮想ケーブル側の再生デバイスを選びます。
4. Discord の `ユーザー設定 > 音声・ビデオ > 入力デバイス` を `CABLE Output` にします。
5. SoundDeck で `Start Pipe` を押します。
6. `Add MP3` で流したい MP3 を追加します。
7. 必要なタイミングで各クリップの `▶` を押します。複数の MP3 を同時に重ねられます。

Discord 側で以下を調整すると、音楽や効果音が途切れにくくなります。

- 入力感度を手動にする
- ノイズ抑制を弱める、または切る
- エコー除去や自動ゲイン制御を必要に応じて切る

## 操作

- `Start Pipe`: マイクを仮想ケーブルへ送出開始
- `Stop Pipe`: 送出停止
- `Add MP3`: MP3 をリストへ追加
- `全停止`: 再生中の MP3 をすべて停止
- `Loop`: クリップ再生時にループ再生
- `Microphone volume`: マイク音量
- `MP3 volume`: MP3 音量
- `Mute microphone`: マイクだけ一時ミュート
- `Lower mic while MP3 plays`: MP3 再生中だけマイク音量を下げる
- 各クリップの `▶`: その MP3 を追加再生
- 各クリップの `■`: その MP3 だけ停止
- 各クリップの音量: 個別の MP3 音量

デバイス選択、音量、ミュート、ducking、ループ設定は自動保存され、次回起動時に復元されます。設定ファイルは `%AppData%\SoundDeck\settings.json` に保存されます。

## 注意

- VC に音を流す前に、相手に許可を取ってください。
- Discord の音声処理設定によっては、音楽が小さくなったり途切れたりします。
- Windows の既定デバイスではなく仮想ケーブルを選ぶと、自分のスピーカーには直接聞こえない場合があります。

