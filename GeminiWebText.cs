namespace AiBurgerClock;

// Menu wording of the Gemini app usage reader (tray / menu-bar menu). Only the labels are shared: each host builds its
// own menu, decides the order and wires the actions. Feedback sentences stay with the host that shows them.
internal static class GeminiWebText
{
    public const string MenuTitle = "Gemini 사용량";
    public const string LoginLabel = "로그인…";
    public const string OffLabel = "끄기";
    public const string LogoutLabel = "로그아웃";
}
