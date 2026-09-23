using SSPen.Annotation;
using SSPen.Settings;
using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="QuickColorHotkeys"/>의 증인 (67단계, A9-3). 바로가기 색상의 고정 조합 <c>Ctrl+Shift+1~6</c>이 등록·충돌 판정·라벨 세 벌·
/// 툴팁 id 프로토콜로 다섯 곳에 따로 적혀 있던 것을 한 소유자로 모았다. 표기는 예전 리터럴과 바이트 단위로 같아야 한다(동작 보존 스냅샷).
/// </summary>
public class QuickColorHotkeysTests
{
    // Win32 수식키 값을 직접 쓴다 — NativeMethods 상수가 아니라 등록되는 값(계약)을 검증한다.
    private const uint CtrlShift = 0x0002 | 0x0004;

    public static TheoryData<int> Slots()
    {
        var data = new TheoryData<int>();
        for (int slot = 0; slot < AppState.QuickColorCount; slot++)
        {
            data.Add(slot);
        }
        return data;
    }

    /// <summary>예전 리터럴 <c>$"Ctrl+Shift+{slot + 1}"</c>과 같은 문자열이다.</summary>
    [Theory]
    [MemberData(nameof(Slots))]
    public void Label_EachSlot_IsCtrlShiftDigit(int slot)
    {
        Assert.Equal($"Ctrl+Shift+{slot + 1}", QuickColorHotkeys.Label(slot));
    }

    /// <summary>예전 등록 값 <c>MOD_CONTROL | MOD_SHIFT</c>, <c>VirtualKeys.D1 + slot</c>과 같다.</summary>
    [Theory]
    [MemberData(nameof(Slots))]
    public void For_EachSlot_UsesCtrlShiftAndDigitVk(int slot)
    {
        Assert.Equal(new HotkeyDef(CtrlShift, 0x31u + (uint)slot), QuickColorHotkeys.For(slot));
    }

    [Fact]
    public void Modifiers_IsCtrlShift()
    {
        Assert.Equal(CtrlShift, QuickColorHotkeys.Modifiers);
    }

    /// <summary>표시명은 1-기반이다 ("퀵컬러 1") — 바인딩 표시명과 충돌 알림이 같은 이름을 쓴다.</summary>
    [Theory]
    [MemberData(nameof(Slots))]
    public void Name_EachSlot_IsOneBasedQuickColorName(int slot)
    {
        Assert.Equal($"{Strings.QuickColorName} {slot + 1}", QuickColorHotkeys.Name(slot));
    }

    /// <summary>툴팁 id의 숫자는 사람이 읽는 1-기반이다 — 예전 <c>$"quickcolor:{slot + 1}"</c>과 같다.</summary>
    [Fact]
    public void TooltipId_IsOneBased()
    {
        Assert.Equal("quickcolor:1", QuickColorHotkeys.TooltipId(0));
        Assert.Equal("quickcolor:6", QuickColorHotkeys.TooltipId(5));
    }

    [Theory]
    [MemberData(nameof(Slots))]
    public void TooltipId_RoundTrips_ThroughTryParse(int slot)
    {
        Assert.True(QuickColorHotkeys.TryParseTooltipId(QuickColorHotkeys.TooltipId(slot), out int parsed));
        Assert.Equal(slot, parsed);
    }

    /// <summary>다른 툴팁 id와 접두만 맞고 숫자가 아닌 id는 거절한다 — 그러면 HotkeyLabel이 핫키 표 조회로 넘어간다.</summary>
    [Theory]
    [InlineData("pen")]
    [InlineData("thickness-pair")]
    [InlineData("quickcolor:x")]
    [InlineData("quickcolor:")]
    [InlineData("QuickColor:1")]
    public void TryParseTooltipId_ForeignId_ReturnsFalse(string id)
    {
        Assert.False(QuickColorHotkeys.TryParseTooltipId(id, out _));
    }

    /// <summary>
    /// 설정 창 안내 문구는 사용자 문자열이라 Strings의 const로 남는다 — 대신 조합을 바꾸면 여기서 빨간불이 켜진다
    /// ("첫 칸 라벨~마지막 칸 번호" 형식).
    /// </summary>
    [Fact]
    public void SettingsQuickColorsHint_MentionsFirstLabelAndLastSlot()
    {
        Assert.Contains($"{QuickColorHotkeys.Label(0)}~{AppState.QuickColorCount}", Strings.SettingsQuickColorsHint);
    }

    /// <summary>여섯 칸의 조합은 서로 다르다 — 겹치면 RegisterHotKey 하나가 실패해 한 칸이 조용히 죽는다.</summary>
    [Fact]
    public void For_AllSlots_AreDistinct()
    {
        var defs = Enumerable.Range(0, AppState.QuickColorCount).Select(QuickColorHotkeys.For).ToList();

        Assert.Equal(defs.Count, defs.Distinct().Count());
    }
}
