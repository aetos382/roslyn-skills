# CLAUDE.md

Claude Code プラグイン（Roslyn アナライザー開発用スキル）とその補助ツールのリポジトリ。
背景・スキルの規約・リリース手順は [README.md](README.md)、evals の詳細は [evals/README.md](evals/README.md) を参照。

## コマンド

```bash
dotnet test                                       # ソリューション全体（RoslynSkills.slnx）
dotnet test tests/Aetos.RoslynSkills.Tools.Tests  # 単一テストプロジェクト
dotnet run --project evals -- list                # evals の一覧。実行は手動、CI では回さない
claude plugin validate . --strict                 # マーケットプレイス マニフェスト
claude plugin validate ./plugin --strict          # プラグイン マニフェストとスキル
```

プラグイン検証はルートと `plugin/` の 2 パスが別物なので、両方流す。

## 構成

- `plugin/` — インストールで配布される唯一の範囲。ここ以外はパッケージに入らない。
- `src/Aetos.RoslynSkills.Tools/` — スキルが `dotnet tool exec` で呼ぶツール。
- `tests/`、`evals/` — それぞれツールのユニットテストと、スキルを持たせたエージェントの end-to-end 評価。
- `Temp/` — evals の実行結果。git 管理外。

## ビルド設定の前提

- ソリューションは `.sln` ではなく `RoslynSkills.slnx`。
- `ImplicitUsings` は無効。`using` は全て明示する。
- 出力は各プロジェクトの `bin`/`obj` ではなく `artifacts/`（`UseArtifactsOutput`）。
- `AnalysisLevel=latest-all` かつ `WarningLevel=9999`。警告は多いが `TreatWarningsAsErrors` は false。
- 中央パッケージ管理。パッケージの追加・更新は `Directory.Packages.props` を編集し、restore 後に `packages.lock.json` を必ずコミットする（CI は `RestoreLockedMode` で復元する）。
- SDK は `global.json` で `rollForward: disable` 固定。テストは Microsoft.Testing.Platform 上の MSTest。

## バージョン ピン

ツールのバージョンは `plugin/skills/*/SKILL.md`、`references/*.md`、`plugin/.claude-plugin/plugin.json` に重複して書かれている。
手で書き換えず `/release <version>` を使う。
