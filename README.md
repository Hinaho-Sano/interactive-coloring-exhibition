# めざめる絵の図書館 / Enchanted Silhouette Library

Azure KinectとUnityでつくる、手のひらで額絵に触れるインタラクティブ展示です。
明るく飾られた動物の絵に触れると、フクロウやキツネが額縁から出て、手でなぞった道を追いかけます。

![展示の背景](preview.png)

## 体験

- 開いた手のひらを絵に約0.5秒重ねると、動物や蝶が出現します。
- フクロウは羽ばたき、キツネは跳ねながら手の道を追います。左右の手に対応しています。
- 動物と蝶は約10秒で元の絵へ戻ります。再び触れると呼び出せます。
- 自分のシルエットは半透明の黒。絵は触れる前から明るく表示されます。
- 得点や制限時間は設けず、触れる・描く・待つ・眺める体験を中心にしています。

## ダウンロードと起動

このリポジトリは、フォルダー構造を保った **EnchantedSilhouetteLibrary-UnityProject.zip** と導入手順を公開しています。
ZIPをダウンロードして展開してください。Unityプロジェクトのルートは展開後の `UnityProject` です。

1. Windows x64、Unity **6000.6.4f1**、Azure Kinect DK、USB 3.0と別系統の電源を用意します。Meta Questは使いません。
2. [Microsoft公式のAzure Kinect Sensor SDK](https://github.com/microsoft/Azure-Kinect-Sensor-SDK/releases/tag/v1.4.2)をインストールします。
3. Unityを開く前に、展開フォルダーの `Setup-Dependencies.ps1` をPowerShellで実行します。

   ```powershell
   .\Setup-Dependencies.ps1
   ```

   スクリプトはインストール済みSDKから必要なDLLをコピーし、MediaPipe Unity Plugin 0.16.3、Googleの手モデル、Microsoftの.NETライブラリを公式配布元から取得します。MediaPipeと手モデルはSHA-256を照合します。
   実行ポリシー等で実行できない場合は、組織・PCの管理者の方針に従ってください。
4. Unity Hubで `UnityProject` を追加し、`Assets/Resonance.unity` を開いてPlayします。
5. **F1 →「背景を登録する（5秒後）」** を押し、5秒間カメラの外へ出ます。戻ったら2〜3m程度の位置で手を映してください。

## 操作

| 操作 | 内容 |
|---|---|
| 手のひらで額絵に触れる | 動物・蝶を呼び出す |
| 手で空中に道を描く | 動物が道を順番に追いかける |
| 手を静かに止める | 蝶が手元に集まる |
| F1 | 背景登録、距離、音量、マウス確認モード |
| R | 動物を絵へ戻してリセット |

Playを停止すると背景登録は失われます。家具が影になる場合は、空の背景を登録し直してください。
マウス確認モードは押しながら絵へ触れる操作です。Kinect以外のカメラアプリは終了してから再生してください。

## 構成

- `Assets/Scripts/ExhibitionKinectInput.cs`: Azure Kinectの映像・深度入力
- `Assets/Scripts/MediaPipeGunPose.cs`: 両手・手のひらの取得（初期試作時のファイル名を保持）
- `Assets/Scripts/MagicLibrary.cs`: 絵と蝶の反応
- `Assets/Scripts/AnimalCompanions.cs`: 動物の経路追従・帰還
- `Assets/Scripts/ExhibitionController.cs`: 展示表示・アニメーション・設定
- `Assets/Resources/Magic/`: 絵、背景、動物の素材
- `ART_PROMPTS.txt`: 内蔵imagegenで作成した素材の生成プロンプト

## 依存ソフトと公開範囲

Microsoft Azure Kinect Sensor SDK、[MediaPipe Unity Plugin](https://github.com/homuler/MediaPipeUnityPlugin)、GoogleのHand Landmarkerモデル、Microsoft .NETライブラリを使用します。
これらのバイナリ・大容量パッケージはZIPに同梱せず、各配布元の利用条件で取得します。詳細は `THIRD_PARTY_NOTICES.md` を参照してください。
UnityのLibrary/Temp/Logs、ユーザー設定、認証情報、カメラ映像・録画、背景の深度データは含めていません。

## 確認状況と制限

C#コンパイルと経路追従・帰還・接触判定などの29項目を検証済みです。
実機での手の認識は照明、距離、設置位置に依存します。動物の動きは4コマの2Dスプライトです。
依存ファイルの取得スクリプトは、公開環境での全件新規ダウンロードをまだ検証していません。
公開だけで任意の再利用ライセンスを付与するものではありません。第三者の素材・ソフトウェアにはそれぞれの条件が適用されます。

