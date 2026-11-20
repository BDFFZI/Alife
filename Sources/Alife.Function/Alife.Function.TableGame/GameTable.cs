using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Alife.Foundation;
using Alife.Framework;

namespace Alife.Function.TableGame;

public class TableSeat
{
    public required string Name { get; init; }
    public Character? Character { get; init; }
    public ChatBot? ChatBot { get; init; }
    public bool IsBriefed { get; set; }

    public bool IsHuman => ChatBot == null;
}

/// <summary>
/// 牌桌基类：管理座位，并在轮到 AI 座位时驱动其对话。
/// 派生类只负责规则与局面描述；每次状态变化都通过 <see cref="Commit"/> 通知牌桌。
/// </summary>
public abstract class GameTable
{
    const int MaxAiAttempts = 3;

    public event Action? Changed;

    public IReadOnlyList<TableSeat> Seats => seats;
    public bool IsPlaying => seats.Length > 0;
    public bool IsPaused { get; private set; }
    public TableSeat? TurnSeat => IsPlaying ? ActiveSeat : null;
    public int HumanIndex => Array.FindIndex(seats, seat => seat.IsHuman);

    public void Start(IReadOnlyList<TableSeat> players)
    {
        seats = players.ToArray();
        IsPaused = false;
        foreach (TableSeat seat in seats)
            seat.IsBriefed = false;

        OnStart();
        NotifyAll(StartNotice);
        Commit();
    }
    public void Stop()
    {
        NotifyAll(StopNotice);
        seats = [];
        IsPaused = false;
        Commit();
    }
    public void Resume()
    {
        IsPaused = false;
        Commit();
    }
    public int IndexOfCharacter(Character character)
    {
        return Array.FindIndex(seats, seat => seat.Character == character);
    }

    protected abstract TableSeat? ActiveSeat { get; }
    protected abstract string Rules { get; }
    protected abstract string StartNotice { get; }
    protected abstract string StopNotice { get; }
    protected abstract void OnStart();
    protected abstract string DescribeTurn(TableSeat seat);

    protected int IndexOfSeat(TableSeat seat)
    {
        return Array.IndexOf(seats, seat);
    }
    protected void Commit()
    {
        version++;
        Changed?.Invoke();
        Kick();
    }
    protected void NotifyAll(string message)
    {
        foreach (TableSeat seat in seats)
            seat.ChatBot?.Poke(message);
    }

    void Kick()
    {
        lock (driveLock)
        {
            if (driving)
                return;
            driving = true;
        }

        _ = DriveAsync();
    }
    async Task DriveAsync()
    {
        while (true)
        {
            TableSeat? seat;
            lock (driveLock)
            {
                seat = IsPlaying && IsPaused == false ? ActiveSeat : null;
                if (seat == null || seat.IsHuman)
                {
                    driving = false;
                    return;
                }
            }

            bool acted = false;
            for (int attempt = 0; attempt < MaxAiAttempts && acted == false && IsPlaying; attempt++)
            {
                int beforeVersion = version;
                await AskAsync(seat);
                acted = version != beforeVersion;
            }

            if (acted == false && IsPlaying)
            {
                IsPaused = true;
                Changed?.Invoke();
            }
        }
    }
    async Task AskAsync(TableSeat seat)
    {
        string message = seat.IsBriefed ? "" : Rules + "\n\n";
        try
        {
            using OccupationMarker thinkingMarker = seat.ChatBot!.LanguageModel!.GetThinkingRequester().Rent($"{seat.Name}正在进行对局");
            await seat.ChatBot!.ChatAsync(message + DescribeTurn(seat), breakLast: false);
        }
        catch (Exception exception)
        {
            AlifeLog.LogError(exception);
        }
        seat.IsBriefed = true;
    }

    TableSeat[] seats = [];
    int version;
    bool driving;
    readonly object driveLock = new();
}
