using System.Runtime.CompilerServices;

// テストプロジェクトから internal な計算ロジック（DesktopComposite など）を検証できるようにする。
// 公開 API を広げずにユニットテストするための InternalsVisibleTo（詳細は /tdd の reference.md）。
[assembly: InternalsVisibleTo("Lukit.Tests")]
