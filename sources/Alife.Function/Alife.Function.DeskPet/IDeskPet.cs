using System;
using System.Numerics;
using System.Threading.Tasks;

namespace Alife.Function.DeskPet;

public interface IDeskPet
{
    public event Action<string>? OnInput;
    public event Action<string>? OnInteracted;

    public string[] SupportedExpressions { get; }
    public string[] SupportedMotions { get; }
    
    public Task ShowUsing(bool isUsing);
    public Task ShowSubtitle(string? subtitle);
    public Task ShowExpression(string? expression);
    public Task ShowMotion(string? motion);
    public Task<Vector2> GetPosition();
    public Task Move(Vector2 offset, float time);
}