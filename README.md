# BepInEx Log Alerts

Sea PowerのBepInExログに出力されたWarning / Error / Fatalを、ゲーム画面の上部中央へ通知するAnchor Chain MODです。

- 表示名：BepInEx Log Alerts
- MOD識別子：`mebiustos.bepinexlogalerts`
- DLL：`BepInExLogAlerts.dll`
- バージョン：0.1.3
- 必須：Sea Power、BepInEx 5.4.x、Anchor Chain

## 動作

MODの初期化時に`ILogListener`を登録し、起動中の通知を蓄積します。初回メニュー表示後、現在のBepInExディスク出力をフラッシュして起動ログを補完し、受信済みログとの件数の重複を整理します。起動順の指定や他MODの変更は必要ありません。

起動完了はHarmony Startup Optimizerの`StartupScreen`と同じ判定です。既存のメニューが有効で`CurrentWindow`があり、ロード画面が存在しないか、その描画カメラが無効であることを確認します。メニューの初回Repaintを待って取り込みを開始し、起動中の警告・エラーを1つの通知にまとめます。この通知は初回のみです。Harmony Startup Optimizer自体への依存はありません。

以後は`ILogListener`で受信し、通常通知を表示します。同じレベル・発生元・本文のログは既定10秒以内なら回数で集約します。通知は最大2件、Warningは6秒、Error/Fatalは10秒で消えます。時間は初回描画から数えます。同じ通知の連発で表示時間を延長し続けることはありません。

通知はクリックやフォーカスを取得しません。通常時は画面表示がなく、ゲームを停止しません。ゲーム速度に左右されない実時間を使います。

## 履歴

`F8`で履歴を開閉します。`Escape`または閉じるボタンでも閉じられます。履歴から各ログを選択すると本文とスタックトレースを確認でき、コピーボタンで発生元・レベル・本文をコピーできます。各行の左端の線はポップアップと同じく、Warningは黄橙、Error／Fatalは赤で示します。選択中の行は背景を明るくして区別します。一覧・詳細はホイールとスクロールバーのドラッグでスクロールできます。

通知が消えても履歴は保持します。既定で200グループまで保存し、それを超えた場合は古い履歴を削除した件数を表示します。ファイル内のログに時刻がないため、過去ログの発生時刻は推測せず「起動時」と表示します。「発生元」はログを出した名前であり、不具合の原因MODを断定するものではありません。

## 個別の通知抑止・解除

履歴でログを選択し、右側の「今後、このログを通知しない」をチェックすると、そのログのポップアップを止められます。チェックを外すと、そのログだけ通知を再開できます。抑止したログも収集・履歴保存を続け、一覧に「通知しない」と表示します。

画面上部の「通知しない設定をすべて解除」で一括解除できます。現在の履歴に存在しない、過去の起動時に指定した設定も解除します。通知抑止の設定はゲーム再起動後も保持します。BepInEx由来のログも既定では対象に残します。

対象はログレベル・発生元・本文の組み合わせです。起動時かプレイ中か、発生時刻、回数には左右されません。本文中の番号などが変わるログは別のログとして扱います。ワイルドカードや部分一致による除外は行いません。

起動時のまとめ通知の件数と通常のポップアップに同じ設定を適用します。全件抑止された場合は起動時ポップアップを出しません。表示中の通知を抑止すると、その通知も取り下げます。解除しても過去のポップアップや過去分の件数は再表示せず、次に発生した分から通知します。履歴の件数は抑止中の発生分も含むため、再開後のポップアップの回数とは異なることがあります。

設定は`BepInEx/config/mebiustos.bepinexlogalerts.mutedlogs`へ初回変更時に保存します。画面からの変更は、保存成功後に反映します。保存に失敗した場合は既存設定を維持し、ログと履歴へ理由を表示します。ファイルが壊れている・読み取れない場合は、既存の初期化エラー処理に従ってログへ理由を出し、無断で初期化・上書きしません。

## 導入

1. Anchor Chainとそのpreloaderを導入してください。
2. [Releases](https://github.com/mebiustos-sea-power/BepInExLogAlerts/releases/latest)から配布ZIPをダウンロードして展開し、`BepInExLogAlerts`フォルダを`Sea Power_Data/StreamingAssets`へコピーします。
3. MODマネージャーでAnchor ChainとBepInEx Log Alertsを有効にし、ゲームを再起動します。

Workshopで配布する場合も、同じパッケージ内容を使用します。このリポジトリの作成やビルドだけでは、ゲームへのインストールやWorkshopへの公開は行いません。

## 設定

初回有効起動時に`BepInEx/config/mebiustos.bepinexlogalerts.cfg`を作成します。設定変更は次回のゲーム起動から反映されます。

| セクション・キー | 既定値 | 意味 |
|---|---|---|
| Logging / IncludeUnityLogs | false | Unity Logも対象にする。ゲーム本体のログが混ざり、原因MODを特定できない場合があります |
| Display / Width | 680 | 1080p基準の通知幅 |
| Display / Top | 20 | 1080p基準の上端からの距離 |
| Display / Scale | 1 | 画面解像度の自動補正に加える倍率 |
| Display / WarningSeconds | 6 | Warningの表示秒数 |
| Display / ErrorSeconds | 10 | Error/Fatalの表示秒数 |
| Display / RepeatSeconds | 10 | 同じログをまとめる間隔 |
| Display / Language | Auto | ゲームの言語に追従。English / Japaneseも指定可能 |
| Input / HistoryKey | F8 | 履歴を開閉するキー |
| Limits / HistoryEntries | 200 | 履歴グループ数の上限 |
| Limits / PendingEntries | 4096 | 未処理ログ数の上限 |
| Limits / StartupLogMegabytes | 8 | 起動ログファイルの読込上限MiB |

## ログ補完の範囲と失敗時の扱い

起動前ログを補完するには、BepInExの`Logging.Disk.Enabled=true`、`AppendLog=false`、および必要なログレベルの保存が必要です。既定設定がこれに該当します。追記モードにはセッションごとの確実な境界がないため、`AppendLog=true`のときはファイルからの補完を行わず、理由をログと画面へ明示します。ディスク出力が無効・アクセス不能・未対応形式の場合も、補完失敗を明示し、直接受信できたログの通知は継続します。

Unityログは既定で対象外です。対象にしても、登録前のUnityログを補完するにはBepInEx側の`Logging.Disk.WriteUnityLog=true`が必要です。通知MOD起動前にファイルへ記録されなかったログは回収できません。

複数のログファイルが存在する場合は、現在のプロセスの実際のディスクライターから読み取り先を取得します。`LogOutput.log`というファイル名だけで別プロセスのログを選択しません。

巨大な起動ログは末尾から指定上限まで取り込み、切り詰めた事実を通知します。1イベントの本文は16384文字まで保存し、超過した本文には切り詰め表示を付けます。受信キューの上限を超えた場合も省略数を通知します。完全な内容は元のログファイルで確認してください。

本MOD自身のログは再受信しません。受信コールバックから例外や再帰ログを発生させず、コールバック内の障害は次の更新処理でログと履歴へ報告します。

本MODやAnchor Chain自体がロードされない場合、および描画できる状態に到達しない場合は、画面通知できません。

## ビルドと検証

Windowsの.NET 10 SDKを使用します。NuGetパッケージのダウンロードは不要です。`Directory.Build.local.props.example`を参考にローカルのゲーム・Anchor Chainの参照先を`Directory.Build.local.props`へ設定してください。

```powershell
.\build.ps1 -Test
```

通常のビルドとパッケージ作成だけなら`-Test`を外します。成果物は`dist/BepInExLogAlerts`、配布ZIPの`dist/BepInExLogAlerts-v0.1.3.zip`、SHA256一覧の`dist/SHA256SUMS.txt`です。明示的に`-InstallLocal`を指定した場合だけ、ゲーム終了状態を確認してStreamingAssetsへインストールします。

検証にはログ解析、起動分と受信分の重複整理、連発・上限・画面停止中の表示時間、およびSea Power付属MonoでのBepInEx実体を用いた統合確認を含めます。Monoの検証は独立したプロセスで行い、ゲーム本体を起動せず、一時ログもゲームのログとは別の場所に作成・削除します。

`StartupScreen.cs`はHarmony Startup Optimizerの同名実装と同じ判定を使用します。`ModActivation.cs`は既存のBulk Identification Requestsと同じMOD有効判定です。

`docs/agent-memory.md`はローカル作業用であり、Git管理・配布対象外です。

## 0.1.1の修正

履歴の選択・閉じる・コピー・スクロールを、新Input Systemから直接処理する方式に変更しました。IMGUIは描画だけに使用し、ゲームがOnGUIへマウスイベントを渡さない場合にも操作できるようにしています。1080p・4K・表示倍率変更時の座標変換、クリックの押下と解放、クリップボードに渡す選択済みログ、スクロールの回帰テストを追加しました。

## 0.1.2の変更

履歴に個別の通知抑止・解除のチェックボックスと一括解除を追加しました。設定を保存し、起動時通知・通常通知・表示中の通知へ共通の判定を適用します。再開時に過去の通知を出し直さず、抑止中のログも履歴には残します。

## 0.1.3の変更

履歴の全行にログレベルに応じた色の線を表示し、ポップアップと色の定義を共通にしました。選択状態は従来の背景強調で示します。

## ソースとリリース

- [ソースコード](https://github.com/mebiustos-sea-power/BepInExLogAlerts)
- [リリース](https://github.com/mebiustos-sea-power/BepInExLogAlerts/releases)

Workshop用アイコンは`assets/workshop/workshop_icon.jpg`で管理します。
