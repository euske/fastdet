# IObjectDetector について

文責: 新山 祐介, 2022-05-10


## 概要

この文書では IObjectDetector API について解説する。
IObjectDetector は Unity上で動作する、物体認識をおこなうための
インターフェイスである。なお、本文書の理解には
Unity および C# の知識が必要である。


## 処理の流れ

IObjectDetector はニューラルネットワークを使って
与えられた画像中にある複数の物体の位置および座標・大きさを推定する。

IObjectDetector インターフェイスには 2種類の実装 (ローカルおよびリモート) が
あり、ローカル版は端末上の GPU/CPU を使って (UnityのBarracudaパッケージを
利用している)、リモート版はサーバ上の GPU を使って計算処理をおこなう。
なおリモートでの認識を使うためにはクライアントとサーバは TCP および
UDP 経由で通信可能である必要がある。(UDP を使っているが、通常の NAT による
フォワーディングをサポートしている。)

基本的な処理の流れは、IObjectDetector インターフェイスに
画像 (Unity では Texture) を渡し、認識された物体の一覧を受けとる
だけである。ただし処理は非同期におこなわれるため、画像を渡しても
すぐに結果が返ってくるわけではない。また、ネットワークの障害等により
結果が返ってこない (タイムアウトする) ケースも存在する。

そのため IObjectDetector では、多くのネットワーク経由の RPC と同様に
「リクエスト (YLRequest)」と「レスポンス (YLResult)」という概念を使っている。
渡された画像には一意な「リクエストID」が付与され、ある程度の時間が
経過してから当該リクエストIDに対する結果が C# イベントを用いて返される。


## 使い方

 1. アプリケーション開始時 (Unity上では、スクリプトの Start() が呼ばれた時点) で
    以下のクラスのどれかを作成する。
    (これらのクラスは同時に使用してもよい)
    また、C# イベントハンドラを設定する。

    void Start() {
        // DummyDetector (ダミークラス、画像によらず同じ認識結果を返す)
        detector = new DummyDetector();
        detector.ResultObtained += detector_resultObtained;
        // LocalYOLODetector (ローカル認識、ニューラルネットワークのモデルを与える)
        // detector = new LocalYOLODetector(yoloModel);
        // RemoteYOLODetector (リモート認識、サーバのURLを与える)
        // detector = new RemoteYOLODetector("rtsp://192.168.1.1:1234/detect");
    }

 2. 定期的にカメラから画像を取得し、IObjectDetector に渡す。
    Unity では Update() メソッドが定期的に呼ばれるため、これを利用する。
    Unity 上における画像は通常 Texture オブジェクトとして扱われるので、
    カメラから Texture オブジェクトを取得し、ProcessImage メソッドを呼ぶ。
    このとき、画像中の認識範囲 (detectArea) および物体認識のしきい値
    (threshold) を指定する。

    void Update() {
        var image = ...;
        var request = detector.ProcessImage(image, area, threshold);
        ...
    }

    注意: 物体認識エンジンがサポートする画像は、つねに正方形である必要がある。
          認識したい画像が正方形でない場合 (通常カメラからの入力) は、
          認識させたい最大の領域を area で指定する。
          なお、この場合の座標は Textureの uv座標系 ([0,1]) が使われる。

    注意: ProcessImage は認識結果を即時返すのではなく、リクエストオブジェクト
          (YLRequest) を返す。

 3. 定期的に detector の Update() メソッドを呼ぶ。
    これは認識エンジンとの通信を監視し、結果が返され次第 C#イベントを発生させる。
    detector に関連づけられたハンドラが実行されるので、取得した
    結果を利用する。

    void Update() {
        ...
        detector.Update();
    }

    // 結果が取得されたときに呼ばれる。
    void detector_resultObtained(object sender, YLResultEventArgs e) {
        // 結果を使用する。
        var result = e.Result;
        ...
    }

    注意: 認識結果は YLObject の配列である。各物体には、種類 (文字列によって表される)
          および矩形 (Rect型) が付与されている。矩形は認識画像 (Texture) の
          uv座標系 ([0,1]) で表現される。
