namespace Lab_Editor;

// ======================================================
// HelpForm - 使い方ガイド（初回起動時の案内 兼 ヘルプメニューから開ける参照）
// Feature: UI改善（提案書 MW-1 / MW-2）
// ======================================================
public class HelpForm : Form
{
    public HelpForm()
    {
        Text = "📖 Lab Editor 使い方ガイド";
        Size = new Size(680, 620);
        MinimumSize = new Size(480, 400);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Meiryo UI", 9);

        // ガイド本文を表示するためのリッチテキストボックス（読み取り専用、枠線なしで背景に馴染ませる）
        var rtb = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            Font = new Font("Meiryo UI", 9.5f),
            BackColor = Color.White,
        };

        // 大見出しを1行追記するローカル関数（青系の太字・大きめフォントで強調する）
        void Heading(string text)
        {
            rtb.SelectionFont = new Font("Meiryo UI", 12, FontStyle.Bold);
            rtb.SelectionColor = Color.FromArgb(30, 90, 160);
            rtb.AppendText(text + "\n");
        }
        // 小見出しを1行追記するローカル関数（黒の太字でHeadingより控えめに強調する）
        void SubHeading(string text)
        {
            rtb.SelectionFont = new Font("Meiryo UI", 10, FontStyle.Bold);
            rtb.SelectionColor = Color.Black;
            rtb.AppendText(text + "\n");
        }
        // 本文段落を追記するローカル関数（通常フォント・グレー寄りの文字色。末尾に空行を1つ加えて段落間隔を作る）
        void Body(string text)
        {
            rtb.SelectionFont = new Font("Meiryo UI", 9.5f);
            rtb.SelectionColor = Color.FromArgb(50, 50, 50);
            rtb.AppendText(text + "\n\n");
        }

        // 以下、ガイド本文を見出し・小見出し・本文の組み合わせで順番に流し込んでいく。
        // 文言（日本語の説明文そのもの）はユーザー向けの表示テキストであり、
        // 各Heading/SubHeading/Body呼び出しの構造自体がガイドの章立てを表している。

        Heading("はじめての方へ：基本の流れ");
        Body(
            "① メイン画面の「新規ステージ」でステージを作る\n" +
            "② 左のタイルパレットから地形タイルを選び、マップにクリック/ドラッグで配置する\n" +
            "③ アセット管理（メニュー「アセット管理」）で敵・ギミック・アイテムを作り、右のリストから選んで配置する\n" +
            "④ 必要ならトリガー（メニューの「トリガー配置」モード）でイベント（メッセージ表示・扉を開く等）を仕込む\n" +
            "⑤ 「プレイ」メニューのテストプレイ（F5）で実際に遊んで確認する\n\n" +
            "この①〜⑤を行き来しながら少しずつ作っていくのが基本の進め方です。保存を忘れても、ほとんどの画面には元に戻す(Ctrl+Z)が効くので、気軽に試してみてください。"
        );

        Heading("画面ごとの役割");
        SubHeading("🗺 メインウィンドウ");
        Body("マップ全体の配置を行う中心画面です。左のタイルパレット・敵/ギミック/アイテムのリストから選び、マップ上に配置します。レイヤーは「メイン地形／装飾（後景・前景）／敵・アイテム等」の4種類があり、今どのレイヤーを編集中かは画面左上に表示されます。");
        SubHeading("🧰 アセット管理");
        Body("敵・ギミック・アイテムの「種類」そのものを作る画面です。画像・当たり判定・HP・タイプ別の挙動パラメータを設定します。「🔍 タイプをカードから選ぶ」ボタンで、番号ではなくアイコンと説明を見ながらタイプを選べます。");
        SubHeading("🧩 パーツ編集 / 挙動スクリプト");
        Body("1体の敵/ギミック/アイテムを複数の画像パーツで組み立てたり（パーツ編集）、ブロックを組み合わせて動きを自作したり（挙動スクリプト）できます。どちらも「▶再生」やテンプレート/ウィザードから始められるので、まず選んで試してみるのがおすすめです。");
        SubHeading("🎞 アニメーション / 🔊 サウンド / 🌄 背景設定");
        Body("それぞれスプライトシートのコマ割り、効果音・BGM、背景の視差スクロールを設定する画面です。いずれもその場でプレビュー（試聴・自動再生）できます。");

        Heading("よく使う操作");
        Body(
            "・Ctrl+Z / Ctrl+Y … 元に戻す／やり直す（マップ、パーツ編集、タイルエディタなど主要な画面で使えます）\n" +
            "・F5 … テストプレイ\n" +
            "・Ctrl+S … 保存\n" +
            "・矢印キー(↑↓←→)を同時押し … ゲーム本体を直接起動する隠しショートカット"
        );

        Heading("困ったときは");
        Body("各編集画面の入力欄にマウスを乗せると、説明のツールチップが出る箇所があります。ブロックエディタのパレットは検索欄で絞り込めます。それでも分からない場合は、似た既存の敵/ギミック/コモンイベントを複製して手を加えるところから始めるのも近道です。");

        // 画面下部に「閉じる」ボタンを配置するための領域（右揃えで表示する）
        var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 46 };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        // 「閉じる」ボタン。保存系ボタンと同じ緑色スタイルを直接指定して統一感を出している
        var btnClose = new Button { Text = "閉じる", AutoSize = true, Padding = new Padding(10, 5, 10, 5), BackColor = Color.FromArgb(40, 167, 69), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        btnClose.Click += (s, e) => Close();
        flow.Controls.Add(btnClose);
        pnlBottom.Controls.Add(flow);
        // Enterキーでも閉じられるよう、既定のAcceptButtonとして登録する
        AcceptButton = btnClose;

        Controls.Add(rtb);
        Controls.Add(pnlBottom);
    }
}
