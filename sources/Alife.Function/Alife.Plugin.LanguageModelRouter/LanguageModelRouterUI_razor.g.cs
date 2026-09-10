using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Alife.Framework;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Rendering;
using AntDesign;

namespace Alife.Plugin.LanguageModelRouter;

public partial class LanguageModelRouterUI : ModuleUIBase<LanguageModelRouter, LanguageModelRouterConfig>, IDisposable
{
    string?[] _detectResults = Array.Empty<string?>();
    List<string>?[] _detectedModels = Array.Empty<List<string>?>();
    int _seq;
    int? _dragSource;

    // 订阅标记，Dispose 时退订静态事件，避免累积订阅（内存泄漏 + 重复刷新）
    bool _subscribed;

    const string Css = @"
.ls-container {
  --ls-bg: #080706;
  --ls-bg2: #120f0a;
  --ls-bg3: #1c1710;
  --ls-gold: #e8c65a;
  --ls-gold-bright: #fff0b0;
  --ls-gold-dim: #c9a43a;
  --ls-amber: #dcb44a;
  --ls-border: #5a4a28;
  --ls-border-soft: #3d3420;
  --ls-text: #f7efd8;
  --ls-text-dim: #e0d2a4;
  --ls-text-mute: #c4b07a;
  --ls-danger: #ef7a68;
  --ls-ok: #a8d47a;
  --ls-crimson: #8b2e2e;
  position: relative;
  max-width: 740px;
  padding: 32px 28px 36px;
  border-radius: 18px;
  isolation: isolate;
  background:
    radial-gradient(ellipse 90% 55% at 50% -15%, rgba(255,220,120,0.22), transparent 55%),
    radial-gradient(ellipse 45% 35% at 100% 0%, rgba(180,40,40,0.08), transparent 50%),
    radial-gradient(ellipse 40% 30% at 0% 100%, rgba(212,175,55,0.1), transparent 50%),
    radial-gradient(ellipse 60% 40% at 80% 90%, rgba(120,60,20,0.12), transparent 55%),
    linear-gradient(165deg, #16120c 0%, #0a0907 45%, #0e0b08 100%);
  border: 1px solid transparent;
  background-clip: padding-box;
  box-shadow:
    0 0 0 1px rgba(232,198,90,0.35),
    0 0 0 2px rgba(20,16,8,0.9),
    0 0 0 3px rgba(232,198,90,0.12),
    0 20px 60px rgba(0,0,0,0.55),
    0 0 80px rgba(232,198,90,0.1),
    inset 0 1px 0 rgba(255,240,176,0.08);
  color: var(--ls-text);
  font-family: 'Segoe UI', 'Microsoft YaHei', 'PingFang SC', system-ui, sans-serif;
  overflow: hidden;
  animation: ls-rise 0.7s cubic-bezier(.2,.8,.2,1);
}
.ls-container::before {
  content: '';
  position: absolute;
  inset: -50%;
  background:
    conic-gradient(from 0deg, transparent 0deg, rgba(255,230,140,0.07) 40deg, transparent 80deg, transparent 180deg, rgba(255,200,80,0.05) 220deg, transparent 280deg);
  animation: ls-aura-spin 18s linear infinite;
  pointer-events: none;
  z-index: 0;
}
.ls-frame {
  position: absolute;
  inset: 7px;
  border: 1px solid rgba(232,198,90,0.18);
  border-radius: 13px;
  pointer-events: none;
  z-index: 1;
  box-shadow: inset 0 0 30px rgba(232,198,90,0.04);
}
.ls-frame::before {
  content: '';
  position: absolute;
  inset: 4px;
  border: 1px dashed rgba(232,198,90,0.1);
  border-radius: 10px;
  animation: ls-dash 20s linear infinite;
}
.ls-border-glow {
  position: absolute;
  inset: 0;
  border-radius: 18px;
  pointer-events: none;
  z-index: 1;
  background: linear-gradient(120deg, transparent 20%, rgba(255,240,176,0.35) 45%, rgba(232,198,90,0.15) 55%, transparent 80%);
  background-size: 250% 250%;
  animation: ls-border-flow 5s linear infinite;
  opacity: 0.55;
  mix-blend-mode: screen;
  -webkit-mask: linear-gradient(#000 0 0) content-box, linear-gradient(#000 0 0);
  mask: linear-gradient(#000 0 0) content-box, linear-gradient(#000 0 0);
  -webkit-mask-composite: xor;
  mask-composite: exclude;
  padding: 1.5px;
}
.ls-particles {
  position: absolute;
  inset: 0;
  pointer-events: none;
  z-index: 1;
  overflow: hidden;
}
.ls-particle {
  position: absolute;
  bottom: -5%;
  width: 3px; height: 3px;
  border-radius: 50%;
  background: radial-gradient(circle at 35% 35%, #fffef5, var(--ls-gold-bright) 45%, var(--ls-gold) 100%);
  box-shadow:
    0 0 6px rgba(255,250,220,1),
    0 0 14px rgba(255,240,176,0.95),
    0 0 28px rgba(232,198,90,0.65),
    0 0 42px rgba(232,198,90,0.3);
  opacity: 0;
  animation: ls-float-up 7s ease-in-out infinite, ls-sparkle 2.2s ease-in-out infinite;
  will-change: transform, opacity;
}
.ls-particle-sm {
  width: 2px; height: 2px;
  box-shadow:
    0 0 4px rgba(255,250,220,0.95),
    0 0 10px rgba(255,240,176,0.8),
    0 0 18px rgba(232,198,90,0.45);
}
.ls-particle-md {
  width: 3px; height: 3px;
}
.ls-particle-lg {
  width: 5px; height: 5px;
  box-shadow:
    0 0 8px rgba(255,255,240,1),
    0 0 18px rgba(255,240,176,1),
    0 0 36px rgba(232,198,90,0.75),
    0 0 56px rgba(232,198,90,0.35);
}
.ls-particle-xl {
  width: 7px; height: 7px;
  box-shadow:
    0 0 10px rgba(255,255,245,1),
    0 0 22px rgba(255,240,176,1),
    0 0 44px rgba(232,198,90,0.85),
    0 0 70px rgba(255,220,120,0.4);
}
.ls-rune {
  position: absolute;
  pointer-events: none;
  z-index: 1;
  color: var(--ls-gold);
  font-size: 12px;
  opacity: 0.18;
  text-shadow: 0 0 12px rgba(232,198,90,0.4);
  animation: ls-rune-drift 12s ease-in-out infinite;
}
.ls-rune-1 { top: 18%; left: 4%;  animation-delay: 0s; }
.ls-rune-2 { top: 42%; right: 3%; animation-delay: 2s; font-size: 14px; }
.ls-rune-3 { bottom: 22%; left: 5%; animation-delay: 4s; }
.ls-rune-4 { top: 70%; right: 6%; animation-delay: 1s; font-size: 11px; }
.ls-corner {
  position: absolute;
  width: 28px; height: 28px;
  pointer-events: none;
  z-index: 2;
}
.ls-corner::before, .ls-corner::after {
  content: '';
  position: absolute;
  background: linear-gradient(90deg, var(--ls-gold-bright), var(--ls-gold));
  box-shadow: 0 0 8px rgba(255,240,176,0.5);
}
.ls-corner-tl { top: 4px; left: 4px; }
.ls-corner-tl::before { top: 0; left: 0; width: 22px; height: 2px; }
.ls-corner-tl::after  { top: 0; left: 0; width: 2px; height: 22px; }
.ls-corner-tr { top: 4px; right: 4px; }
.ls-corner-tr::before { top: 0; right: 0; width: 22px; height: 2px; }
.ls-corner-tr::after  { top: 0; right: 0; width: 2px; height: 22px; }
.ls-corner-bl { bottom: 4px; left: 4px; }
.ls-corner-bl::before { bottom: 0; left: 0; width: 22px; height: 2px; }
.ls-corner-bl::after  { bottom: 0; left: 0; width: 2px; height: 22px; }
.ls-corner-br { bottom: 4px; right: 4px; }
.ls-corner-br::before { bottom: 0; right: 0; width: 22px; height: 2px; }
.ls-corner-br::after  { bottom: 0; right: 0; width: 2px; height: 22px; }
.ls-corner-gem {
  position: absolute;
  width: 6px; height: 6px;
  border-radius: 50%;
  background: radial-gradient(circle at 35% 35%, #fff8d0, var(--ls-gold));
  box-shadow: 0 0 10px rgba(255,240,176,0.8);
  animation: ls-pulse 2.5s ease-in-out infinite;
}
.ls-corner-tl .ls-corner-gem { top: -1px; left: -1px; }
.ls-corner-tr .ls-corner-gem { top: -1px; right: -1px; }
.ls-corner-bl .ls-corner-gem { bottom: -1px; left: -1px; }
.ls-corner-br .ls-corner-gem { bottom: -1px; right: -1px; }
.ls-inner { position: relative; z-index: 3; }

@keyframes ls-rise {
  from { opacity: 0; transform: translateY(14px) scale(0.985); filter: blur(4px); }
  to   { opacity: 1; transform: none; filter: none; }
}
@keyframes ls-pulse {
  0%, 100% { opacity: 0.45; filter: brightness(0.9); }
  50% { opacity: 1; filter: brightness(1.3); }
}
@keyframes ls-shimmer {
  0% { background-position: -200% center; }
  100% { background-position: 200% center; }
}
@keyframes ls-breathe {
  0%, 100% { box-shadow: 0 0 0 0 rgba(232,198,90,0), 0 0 10px rgba(232,198,90,0.2); }
  50% { box-shadow: 0 0 0 4px rgba(232,198,90,0.1), 0 0 24px rgba(255,240,176,0.45); }
}
@keyframes ls-glow-line {
  0%, 100% { opacity: 0.35; }
  50% { opacity: 1; }
}
@keyframes ls-seal-spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}
@keyframes ls-seal-spin-rev {
  from { transform: rotate(360deg); }
  to { transform: rotate(0deg); }
}
@keyframes ls-aura-spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}
@keyframes ls-border-flow {
  0% { background-position: 0% 50%; }
  100% { background-position: 200% 50%; }
}
@keyframes ls-float-up {
  0%   { bottom: -5%; opacity: 0; transform: translateX(0) scale(0.6); }
  15%  { opacity: 0.85; }
  50%  { opacity: 0.5; transform: translateX(8px) scale(1); }
  100% { bottom: 105%; opacity: 0; transform: translateX(-6px) scale(0.4); }
}
@keyframes ls-rune-drift {
  0%, 100% { transform: translateY(0) rotate(0deg); opacity: 0.12; }
  50% { transform: translateY(-10px) rotate(8deg); opacity: 0.28; }
}
@keyframes ls-dash {
  to { stroke-dashoffset: -40; }
}
@keyframes ls-ring-pulse {
  0%, 100% { transform: scale(1); opacity: 0.5; }
  50% { transform: scale(1.08); opacity: 0.9; }
}
@keyframes ls-title-glow {
  0%, 100% { filter: drop-shadow(0 0 8px rgba(232,198,90,0.3)); }
  50% { filter: drop-shadow(0 0 18px rgba(255,240,176,0.65)); }
}
@keyframes ls-chip-shine {
  0% { left: -60%; }
  100% { left: 120%; }
}
@keyframes ls-fade-in {
  from { opacity: 0; transform: translateY(6px); }
  to { opacity: 1; transform: none; }
}

/* ===== Title / Crest ===== */
.ls-title-wrap {
  text-align: center;
  margin-bottom: 20px;
  padding: 8px 0 18px;
  position: relative;
}
.ls-crest {
  position: relative;
  width: 72px; height: 72px;
  margin: 0 auto 12px;
}
.ls-crest-ring {
  position: absolute;
  inset: 0;
  border-radius: 50%;
  border: 1.5px solid rgba(232,198,90,0.45);
  box-shadow: 0 0 16px rgba(232,198,90,0.25), inset 0 0 12px rgba(232,198,90,0.1);
  animation: ls-seal-spin 14s linear infinite;
}
.ls-crest-ring::before {
  content: '';
  position: absolute;
  top: -3px; left: 50%;
  width: 6px; height: 6px;
  margin-left: -3px;
  border-radius: 50%;
  background: var(--ls-gold-bright);
  box-shadow: 0 0 10px rgba(255,240,176,0.9);
}
.ls-crest-ring-inner {
  position: absolute;
  inset: 8px;
  border-radius: 50%;
  border: 1px dashed rgba(232,198,90,0.35);
  animation: ls-seal-spin-rev 10s linear infinite;
}
.ls-crest-core {
  position: absolute;
  inset: 16px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 18px;
  color: #1a1408;
  font-weight: 800;
  background:
    radial-gradient(circle at 35% 30%, #fff6c8, var(--ls-gold) 45%, var(--ls-amber) 100%);
  box-shadow:
    0 0 20px rgba(255,240,176,0.55),
    0 0 40px rgba(232,198,90,0.25),
    inset 0 1px 2px rgba(255,255,255,0.5);
  animation: ls-breathe 3s ease-in-out infinite;
  letter-spacing: 0;
}
.ls-crest-halo {
  position: absolute;
  inset: -6px;
  border-radius: 50%;
  border: 1px solid rgba(255,240,176,0.15);
  animation: ls-ring-pulse 3.5s ease-in-out infinite;
}
.ls-title-ornament {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 12px;
  margin-bottom: 10px;
  color: var(--ls-gold-bright);
  font-size: 11px;
  letter-spacing: 5px;
  text-shadow: 0 0 12px rgba(255,240,176,0.35);
}
.ls-title-ornament::before,
.ls-title-ornament::after {
  content: '';
  flex: 1;
  max-width: 90px;
  height: 1px;
  background: linear-gradient(90deg, transparent, var(--ls-gold-bright), transparent);
  box-shadow: 0 0 8px rgba(255,240,176,0.4);
  animation: ls-glow-line 2.5s ease-in-out infinite;
}
.ls-title {
  font-size: 19px;
  font-weight: 800;
  letter-spacing: 1.5px;
  background: linear-gradient(100deg,
    #a88420 0%,
    var(--ls-gold-bright) 25%,
    #fff 40%,
    var(--ls-gold-bright) 55%,
    var(--ls-gold) 70%,
    var(--ls-gold-dim) 100%);
  background-size: 220% auto;
  -webkit-background-clip: text;
  background-clip: text;
  color: transparent;
  animation: ls-shimmer 4s linear infinite, ls-title-glow 3s ease-in-out infinite;
}
.ls-title-sub {
  margin-top: 8px;
  font-size: 11px;
  color: var(--ls-text-dim);
  letter-spacing: 4px;
  text-shadow: 0 0 10px rgba(232,198,90,0.2);
}
.ls-title-bar {
  width: 120px;
  height: 2px;
  margin: 12px auto 0;
  background: linear-gradient(90deg, transparent, var(--ls-gold-bright), var(--ls-gold), transparent);
  box-shadow: 0 0 12px rgba(255,240,176,0.5);
  border-radius: 2px;
  animation: ls-glow-line 2s ease-in-out infinite;
}

/* ===== Alert / Oracle ===== */
.ls-alert {
  position: relative;
  margin-bottom: 22px;
  padding: 18px 16px 16px;
  border-radius: 14px;
  overflow: hidden;
  border: 1px solid rgba(232,198,90,0.4);
  background:
    radial-gradient(ellipse 80% 90% at 0% 0%, rgba(255,230,140,0.14), transparent 55%),
    radial-gradient(ellipse 50% 60% at 100% 100%, rgba(180,40,40,0.08), transparent 50%),
    linear-gradient(155deg, rgba(36,28,14,0.96) 0%, rgba(10,8,6,0.98) 100%);
  box-shadow:
    0 0 0 1px rgba(232,198,90,0.08) inset,
    0 10px 36px rgba(0,0,0,0.4),
    0 0 50px rgba(232,198,90,0.1);
}
.ls-alert::before {
  content: '';
  position: absolute;
  inset: 0;
  background: linear-gradient(105deg, transparent 35%, rgba(255,240,176,0.1) 50%, transparent 65%);
  background-size: 220% 100%;
  animation: ls-shimmer 5s linear infinite;
  pointer-events: none;
}
.ls-alert::after {
  content: '';
  position: absolute;
  left: 0; top: 0; bottom: 0;
  width: 3px;
  background: linear-gradient(180deg, transparent, var(--ls-gold-bright), var(--ls-gold), transparent);
  box-shadow: 0 0 16px rgba(255,240,176,0.7);
}
.ls-alert-header {
  position: relative;
  z-index: 1;
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 14px;
  padding-bottom: 10px;
  border-bottom: 1px solid rgba(232,198,90,0.2);
}
.ls-alert-header-icon {
  width: 26px; height: 26px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 12px;
  color: #1a1408;
  background: linear-gradient(135deg, #fff6c8, var(--ls-gold), var(--ls-amber));
  box-shadow: 0 0 18px rgba(255,240,176,0.55);
  animation: ls-breathe 2.8s ease-in-out infinite;
  flex-shrink: 0;
}
.ls-alert-header-title {
  font-size: 13px;
  font-weight: 800;
  letter-spacing: 3px;
  color: var(--ls-gold-bright);
  text-shadow: 0 0 14px rgba(255,240,176,0.4);
}
.ls-alert-header-line {
  flex: 1;
  height: 1px;
  background: linear-gradient(90deg, rgba(232,198,90,0.5), transparent);
  box-shadow: 0 0 6px rgba(232,198,90,0.3);
}
.ls-alert-body {
  position: relative;
  z-index: 1;
  display: flex;
  flex-direction: column;
  gap: 9px;
}
.ls-alert-line {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  padding: 10px 12px;
  border-radius: 10px;
  background: linear-gradient(135deg, rgba(232,198,90,0.06), rgba(0,0,0,0.15));
  border: 1px solid rgba(232,198,90,0.14);
  transition: all 0.3s cubic-bezier(.2,.8,.2,1);
  animation: ls-fade-in 0.5s ease both;
}
.ls-alert-line:nth-child(1) { animation-delay: 0.06s; }
.ls-alert-line:nth-child(2) { animation-delay: 0.14s; }
.ls-alert-line:nth-child(3) { animation-delay: 0.22s; }
.ls-alert-line:nth-child(4) { animation-delay: 0.3s; }
.ls-alert-line:hover {
  border-color: rgba(255,240,176,0.45);
  background: linear-gradient(135deg, rgba(232,198,90,0.14), rgba(40,30,10,0.4));
  transform: translateX(5px) scale(1.01);
  box-shadow: 0 0 22px rgba(232,198,90,0.15), inset 0 0 20px rgba(232,198,90,0.04);
}
.ls-alert-mark {
  flex-shrink: 0;
  width: 22px; height: 22px;
  margin-top: 1px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 11px;
  color: var(--ls-gold-bright);
  background: radial-gradient(circle at 40% 35%, rgba(255,240,176,0.3), rgba(20,16,8,0.95));
  border: 1px solid rgba(232,198,90,0.55);
  box-shadow: 0 0 12px rgba(232,198,90,0.3);
  text-shadow: 0 0 8px rgba(255,240,176,0.6);
}
.ls-alert-line-warn {
  background: linear-gradient(135deg, rgba(232,198,90,0.12), rgba(80,40,10,0.25));
  border-color: rgba(255,240,176,0.35);
  box-shadow: 0 0 24px rgba(232,198,90,0.1) inset;
}
.ls-alert-line-warn .ls-alert-mark {
  color: #1a1408;
  background: linear-gradient(135deg, #fff6c8, var(--ls-gold));
  border-color: var(--ls-gold-bright);
  box-shadow: 0 0 18px rgba(255,240,176,0.55);
  animation: ls-pulse 2.2s ease-in-out infinite;
}
.ls-alert-text {
  flex: 1;
  font-size: 12.5px;
  line-height: 1.7;
  color: var(--ls-text);
}

/* ===== Section ===== */
.ls-section {
  margin-top: 24px;
  position: relative;
  padding: 14px 14px 12px;
  border-radius: 12px;
  background: linear-gradient(160deg, rgba(232,198,90,0.05) 0%, transparent 50%);
  border: 1px solid rgba(232,198,90,0.1);
  box-shadow: inset 0 1px 0 rgba(255,240,176,0.04);
}
.ls-section-title {
  display: flex;
  align-items: center;
  gap: 10px;
  font-size: 13.5px;
  font-weight: 800;
  color: var(--ls-gold-bright);
  margin: 0 0 14px;
  padding-bottom: 10px;
  border-bottom: 1px solid rgba(232,198,90,0.18);
  letter-spacing: 1px;
  text-shadow: 0 0 12px rgba(255,240,176,0.25);
}
.ls-section-title::before {
  content: '❖';
  font-size: 12px;
  color: var(--ls-gold);
  text-shadow: 0 0 10px rgba(232,198,90,0.6);
  animation: ls-pulse 3.5s ease-in-out infinite;
}
.ls-section-title::after {
  content: '';
  flex: 1;
  height: 1px;
  background: linear-gradient(90deg, rgba(232,198,90,0.5), transparent 80%);
  box-shadow: 0 0 6px rgba(232,198,90,0.3);
  margin-left: 4px;
}

/* Labels / hints */
.ls-label {
  font-weight: 700;
  margin-bottom: 5px;
  margin-top: 10px;
  font-size: 12.5px;
  color: var(--ls-text);
  letter-spacing: 0.4px;
  text-shadow: 0 0 8px rgba(232,198,90,0.1);
}
.ls-hint {
  font-size: 11.5px;
  color: var(--ls-text-dim);
  margin: 5px 0 12px 2px;
  line-height: 1.65;
  white-space: pre-line;
  padding-left: 8px;
  border-left: 2px solid rgba(232,198,90,0.2);
}

/* Status / badges */
.ls-status-row {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  margin-bottom: 12px;
  font-size: 12.5px;
  color: var(--ls-text);
}
.ls-badge {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 3px 12px;
  border-radius: 999px;
  font-size: 11px;
  font-weight: 700;
  letter-spacing: 0.4px;
  border: 1px solid transparent;
}
.ls-badge-on {
  color: var(--ls-gold-bright);
  background: linear-gradient(135deg, rgba(232,198,90,0.18), rgba(232,198,90,0.06));
  border-color: rgba(232,198,90,0.5);
  box-shadow: 0 0 14px rgba(232,198,90,0.2);
}
.ls-badge-off {
  color: var(--ls-text-dim);
  background: rgba(60,50,30,0.5);
  border-color: var(--ls-border);
}
.ls-badge-active {
  color: #1a1408;
  background: linear-gradient(135deg, #fff6c8, var(--ls-gold), var(--ls-amber));
  border-color: var(--ls-gold-bright);
  box-shadow: 0 0 18px rgba(255,240,176,0.45);
  animation: ls-breathe 2.4s ease-in-out infinite;
}

/* Toggle */
.ls-toggle-row {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  margin-bottom: 12px;
  padding: 12px 14px;
  border-radius: 10px;
  background: linear-gradient(135deg, rgba(232,198,90,0.07), rgba(0,0,0,0.2));
  border: 1px solid rgba(232,198,90,0.16);
  transition: all 0.28s ease;
  position: relative;
  overflow: hidden;
}
.ls-toggle-row::before {
  content: '';
  position: absolute;
  left: 0; top: 0; bottom: 0;
  width: 2px;
  background: linear-gradient(180deg, transparent, var(--ls-gold), transparent);
  opacity: 0;
  transition: opacity 0.25s;
}
.ls-toggle-row:hover {
  border-color: rgba(255,240,176,0.4);
  background: linear-gradient(135deg, rgba(232,198,90,0.12), rgba(30,24,12,0.5));
  box-shadow: 0 0 20px rgba(232,198,90,0.1);
  transform: translateX(2px);
}
.ls-toggle-row:hover::before { opacity: 1; }
.ls-toggle-row input[type=checkbox] {
  appearance: none;
  -webkit-appearance: none;
  width: 20px; height: 20px;
  margin-top: 1px;
  flex-shrink: 0;
  border: 1.5px solid var(--ls-gold-dim);
  border-radius: 5px;
  background: radial-gradient(circle at 40% 35%, #1a1610, #0a0907);
  cursor: pointer;
  position: relative;
  transition: all 0.25s;
  box-shadow: inset 0 0 6px rgba(0,0,0,0.5);
}
.ls-toggle-row input[type=checkbox]:hover {
  border-color: var(--ls-gold-bright);
  box-shadow: 0 0 10px rgba(232,198,90,0.3);
}
.ls-toggle-row input[type=checkbox]:checked {
  background: linear-gradient(135deg, #fff6c8, var(--ls-gold), var(--ls-amber));
  border-color: var(--ls-gold-bright);
  box-shadow: 0 0 16px rgba(255,240,176,0.55);
}
.ls-toggle-row input[type=checkbox]:checked::after {
  content: '✦';
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 11px;
  color: #1a1408;
  font-weight: bold;
  text-shadow: 0 0 4px rgba(255,255,255,0.4);
}
.ls-toggle-row label {
  font-size: 13px;
  color: var(--ls-text);
  cursor: pointer;
  line-height: 1.55;
  user-select: none;
  font-weight: 500;
}

/* Buttons */
.ls-btn-row {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
  margin-bottom: 8px;
}
.ls-btn {
  position: relative;
  padding: 7px 18px;
  border-radius: 9px;
  border: 1px solid rgba(232,198,90,0.35);
  background: linear-gradient(180deg, #221c12 0%, #100e0a 100%);
  color: var(--ls-text);
  cursor: pointer;
  font-size: 12.5px;
  font-weight: 700;
  letter-spacing: 0.4px;
  transition: all 0.28s cubic-bezier(.2,.8,.2,1);
  overflow: hidden;
  box-shadow: 0 2px 8px rgba(0,0,0,0.3), inset 0 1px 0 rgba(255,240,176,0.06);
}
.ls-btn::before {
  content: '';
  position: absolute;
  inset: 0;
  background: linear-gradient(105deg, transparent 25%, rgba(255,240,176,0.18) 50%, transparent 75%);
  background-size: 200% 100%;
  opacity: 0;
  transition: opacity 0.2s;
}
.ls-btn::after {
  content: '';
  position: absolute;
  inset: 0;
  border-radius: 9px;
  padding: 1px;
  background: linear-gradient(135deg, transparent, rgba(255,240,176,0.4), transparent);
  -webkit-mask: linear-gradient(#000 0 0) content-box, linear-gradient(#000 0 0);
  mask: linear-gradient(#000 0 0) content-box, linear-gradient(#000 0 0);
  -webkit-mask-composite: xor;
  mask-composite: exclude;
  opacity: 0;
  transition: opacity 0.25s;
}
.ls-btn:hover {
  border-color: var(--ls-gold-bright);
  color: var(--ls-gold-bright);
  box-shadow: 0 0 20px rgba(232,198,90,0.3), 0 4px 12px rgba(0,0,0,0.35);
  transform: translateY(-2px);
  text-shadow: 0 0 10px rgba(255,240,176,0.35);
}
.ls-btn:hover::before {
  opacity: 1;
  animation: ls-shimmer 1.2s linear infinite;
}
.ls-btn:hover::after { opacity: 1; }
.ls-btn-active {
  border-color: var(--ls-gold-bright);
  color: #1a1408;
  background: linear-gradient(135deg, #fff6c8 0%, var(--ls-gold) 45%, var(--ls-amber) 100%);
  box-shadow: 0 0 22px rgba(255,240,176,0.5), 0 4px 10px rgba(0,0,0,0.35);
  animation: ls-breathe 2.6s ease-in-out infinite;
  text-shadow: 0 1px 0 rgba(255,255,255,0.3);
}
.ls-btn-active:hover {
  color: #1a1408;
  border-color: #fff6c8;
  transform: translateY(-2px) scale(1.03);
}
.ls-btn-dim {
  opacity: 0.7;
  color: var(--ls-text-dim);
}
.ls-add-row {
  margin-top: 14px;
  display: flex;
  gap: 8px;
  align-items: center;
  flex-wrap: wrap;
}
.ls-btn-add {
  padding: 8px 18px;
  border-radius: 9px;
  border: 1px dashed rgba(168,212,122,0.5);
  background: rgba(40,54,26,0.35);
  color: var(--ls-ok);
  cursor: pointer;
  font-size: 13px;
  font-weight: 600;
  font-family: inherit;
  transition: all 0.25s ease;
}
.ls-btn-add:hover:not(:disabled) {
  border-color: var(--ls-ok);
  box-shadow: 0 0 16px rgba(168,212,122,0.3);
  transform: translateY(-1px);
}
.ls-btn-add:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
.ls-btn-remove {
  border-radius: 7px;
  border: 1px solid rgba(239,122,104,0.4);
  background: rgba(90,30,24,0.4);
  color: var(--ls-danger);
  cursor: pointer;
  font-family: inherit;
  transition: all 0.25s ease;
}
.ls-btn-remove:hover {
  border-color: var(--ls-danger);
  box-shadow: 0 0 12px rgba(239,122,104,0.35);
}
.ls-btn-probe {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  padding: 6px 16px;
  border-radius: 8px;
  border: 1px solid rgba(232,198,90,0.5);
  background: linear-gradient(180deg, rgba(232,198,90,0.16) 0%, rgba(16,12,8,0.9) 100%);
  color: var(--ls-gold-bright);
  cursor: pointer;
  font-size: 12px;
  font-weight: 700;
  letter-spacing: 0.4px;
  transition: all 0.28s ease;
  box-shadow: 0 0 12px rgba(232,198,90,0.12), inset 0 1px 0 rgba(255,240,176,0.1);
  position: relative;
  overflow: hidden;
}
.ls-btn-probe:hover {
  background: linear-gradient(180deg, rgba(255,240,176,0.28) 0%, rgba(40,30,12,0.95) 100%);
  border-color: var(--ls-gold-bright);
  box-shadow: 0 0 22px rgba(255,240,176,0.35);
  color: #fff8d0;
  transform: translateY(-1px);
}
.ls-btn-probe-icon {
  display: inline-block;
  font-size: 11px;
  color: var(--ls-gold-bright);
  text-shadow: 0 0 10px rgba(255,240,176,0.7);
  animation: ls-pulse 2.5s ease-in-out infinite;
}

/* Probe */
.ls-probe { margin: 10px 0 14px; }
.ls-probe-result { font-size: 11.5px; margin-left: 10px; font-weight: 600; }
.ls-probe-ok { color: var(--ls-ok); text-shadow: 0 0 8px rgba(168,212,122,0.35); }
.ls-probe-fail { color: var(--ls-danger); text-shadow: 0 0 8px rgba(239,122,104,0.35); }
.ls-probe-select-row {
  margin-top: 10px;
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}
.ls-probe-select-row span { font-size: 12px; color: var(--ls-text-dim); }
.ls-select {
  padding: 5px 10px;
  border-radius: 8px;
  border: 1px solid var(--ls-border);
  background: linear-gradient(180deg, #14110c, #0a0907);
  color: var(--ls-text);
  font-size: 12.5px;
  outline: none;
  cursor: pointer;
  max-width: 100%;
  transition: all 0.2s;
}
.ls-select:hover, .ls-select:focus {
  border-color: var(--ls-gold-bright);
  box-shadow: 0 0 14px rgba(232,198,90,0.25);
}
.ls-select option { background: #14110c; color: var(--ls-text); }

/* Details */
.ls-details {
  margin: 10px 0;
  border: 1px solid rgba(232,198,90,0.18);
  border-radius: 12px;
  padding: 0;
  background:
    linear-gradient(155deg, rgba(232,198,90,0.07) 0%, transparent 45%),
    linear-gradient(180deg, #16120c, #0e0c09);
  overflow: hidden;
  transition: all 0.3s ease;
  box-shadow: 0 2px 12px rgba(0,0,0,0.25);
}
.ls-details:hover {
  border-color: rgba(232,198,90,0.4);
  box-shadow: 0 0 24px rgba(232,198,90,0.1);
}
.ls-details[open] {
  border-color: rgba(255,240,176,0.4);
  box-shadow: 0 0 28px rgba(232,198,90,0.14), inset 0 0 30px rgba(232,198,90,0.03);
}
.ls-details summary {
  cursor: pointer;
  font-weight: 800;
  font-size: 13.5px;
  color: var(--ls-text);
  padding: 12px 16px;
  user-select: none;
  list-style: none;
  display: flex;
  align-items: center;
  gap: 10px;
  transition: all 0.25s;
  letter-spacing: 0.3px;
}
.ls-details summary::-webkit-details-marker { display: none; }
.ls-details summary:hover {
  color: var(--ls-gold-bright);
  background: linear-gradient(90deg, rgba(232,198,90,0.1), transparent);
  text-shadow: 0 0 12px rgba(255,240,176,0.3);
}
.ls-arrow {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 18px; height: 18px;
  font-size: 9px;
  color: #1a1408;
  background: linear-gradient(135deg, var(--ls-gold-bright), var(--ls-gold));
  border-radius: 4px;
  transition: transform 0.3s cubic-bezier(.2,.8,.2,1);
  box-shadow: 0 0 10px rgba(232,198,90,0.35);
  flex-shrink: 0;
}
.ls-details[open] .ls-arrow {
  transform: rotate(90deg);
  box-shadow: 0 0 14px rgba(255,240,176,0.55);
}
.ls-details-content {
  padding: 6px 16px 16px;
  border-top: 1px solid rgba(232,198,90,0.15);
  background: linear-gradient(180deg, rgba(232,198,90,0.03), transparent);
}

/* Feature strip */
.ls-feature-strip {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-bottom: 18px;
  justify-content: center;
}
.ls-feature-chip {
  position: relative;
  font-size: 11px;
  padding: 5px 14px;
  border-radius: 999px;
  border: 1px solid rgba(232,198,90,0.45);
  color: var(--ls-gold-bright);
  background: linear-gradient(135deg, rgba(232,198,90,0.14), rgba(20,16,8,0.6));
  letter-spacing: 1px;
  font-weight: 700;
  overflow: hidden;
  box-shadow: 0 0 12px rgba(232,198,90,0.1);
  transition: all 0.25s;
  text-shadow: 0 0 8px rgba(255,240,176,0.25);
}
.ls-feature-chip::after {
  content: '';
  position: absolute;
  top: 0; left: -60%;
  width: 40%; height: 100%;
  background: linear-gradient(90deg, transparent, rgba(255,255,255,0.2), transparent);
  transform: skewX(-20deg);
  animation: ls-chip-shine 3.5s ease-in-out infinite;
}
.ls-feature-chip:hover {
  border-color: var(--ls-gold-bright);
  box-shadow: 0 0 18px rgba(255,240,176,0.35);
  transform: translateY(-2px);
  color: #fff8d0;
}

/* Seal divider */
.ls-seal {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 14px;
  margin: 20px 0 8px;
  color: var(--ls-gold);
  font-size: 13px;
  opacity: 0.75;
}
.ls-seal::before,
.ls-seal::after {
  content: '';
  flex: 1;
  height: 1px;
  background: linear-gradient(90deg, transparent, var(--ls-gold-bright), transparent);
  box-shadow: 0 0 8px rgba(255,240,176,0.35);
}
.ls-seal-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 28px; height: 28px;
  border-radius: 50%;
  border: 1px solid rgba(232,198,90,0.4);
  background: radial-gradient(circle at 40% 35%, rgba(255,240,176,0.2), rgba(10,8,6,0.9));
  box-shadow: 0 0 16px rgba(232,198,90,0.25);
  animation: ls-seal-spin 16s linear infinite;
  font-size: 13px;
  text-shadow: 0 0 8px rgba(255,240,176,0.5);
}

/* AntDesign overrides */
.ls-container .ant-input,
.ls-container .ant-input-affix-wrapper {
  background: linear-gradient(180deg, #12100c, #0a0907) !important;
  border-color: rgba(232,198,90,0.28) !important;
  color: var(--ls-text) !important;
  border-radius: 9px !important;
  transition: border-color 0.2s, box-shadow 0.2s !important;
  box-shadow: inset 0 1px 4px rgba(0,0,0,0.35) !important;
}
.ls-container .ant-input::placeholder {
  color: var(--ls-text-dim) !important;
  opacity: 0.7 !important;
}
.ls-container .ant-input:hover,
.ls-container .ant-input-affix-wrapper:hover {
  border-color: var(--ls-gold) !important;
  box-shadow: 0 0 12px rgba(232,198,90,0.15), inset 0 1px 4px rgba(0,0,0,0.35) !important;
}
.ls-container .ant-input:focus,
.ls-container .ant-input-focused,
.ls-container .ant-input-affix-wrapper-focused,
.ls-container .ant-input-affix-wrapper:focus {
  border-color: var(--ls-gold-bright) !important;
  box-shadow: 0 0 0 2px rgba(232,198,90,0.2), 0 0 18px rgba(255,240,176,0.15) !important;
}
.ls-container .ant-input-password-icon,
.ls-container .anticon {
  color: var(--ls-gold) !important;
}
.ls-container .ant-input,
.ls-container .ant-input-affix-wrapper input {
  font-size: 13px !important;
}
.ls-container .ant-input-affix-wrapper > input.ant-input {
  background: transparent !important;
  color: var(--ls-text) !important;
}
.ls-groups {
  display: flex;
  flex-direction: column;
  gap: 14px;
}
.ls-group-card {
  border: 1px solid var(--ls-border-soft);
  border-radius: 12px;
  padding: 12px 14px 14px;
  background: linear-gradient(180deg, rgba(232,198,90,0.06), rgba(0,0,0,0.18));
  box-shadow: 0 2px 12px rgba(0,0,0,0.25);
  transition: border-color .2s, box-shadow .2s, opacity .2s;
}
.ls-group-card:hover {
  border-color: var(--ls-border);
}
.ls-group-card[draggable=""true""] {
  cursor: grab;
}
.ls-group-card[draggable=""true""]:active {
  cursor: grabbing;
}
.ls-group-card-dragging {
  opacity: 0.45;
  border-color: var(--ls-gold-dim);
  box-shadow: 0 0 0 1px var(--ls-gold-dim), 0 0 24px rgba(232,198,90,0.22);
}
.ls-group-card-head {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 10px;
}
.ls-drag-handle {
  color: var(--ls-gold);
  font-size: 15px;
  line-height: 1;
  cursor: grab;
  user-select: none;
  opacity: 0.85;
  transition: opacity .2s, transform .2s;
}
.ls-group-card:hover .ls-drag-handle {
  opacity: 1;
  transform: translateX(1px);
}
.ls-group-card-title {
  flex: 1;
  font-weight: 600;
  font-size: 13.5px;
  color: var(--ls-text);
  letter-spacing: 0.5px;
}
.ls-badge-main {
  background: linear-gradient(135deg, rgba(232,198,90,0.32), rgba(232,198,90,0.12));
  border-color: var(--ls-gold-dim);
  color: var(--ls-gold-bright);
  text-shadow: 0 0 10px rgba(255,240,176,0.5);
  box-shadow: 0 0 14px rgba(232,198,90,0.25);
}

";


    protected override void OnInitialized()
    {
        if (!_subscribed)
        {
            LanguageModelRouter.OnGroupChanged += OnGroupChangedHandler;
            _subscribed = true;
        }
    }

    void OnGroupChangedHandler() => InvokeAsync(StateHasChanged);

    // ModuleUIBase 未暴露 Dispose(bool)，改用显式 IDisposable 实现退订静态事件。
    // Blazor 渲染器释放组件时会检测 IDisposable 并调用（接口重新实现，基类是否实现该接口均可）。
    void IDisposable.Dispose()
    {
        if (_subscribed)
        {
            LanguageModelRouter.OnGroupChanged -= OnGroupChangedHandler;
            _subscribed = false;
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        if (Configuration == null)
        {
            b.AddContent(0, "Configuration NULL");
            return;
        }

        _seq = 0;

        b.OpenElement(_seq++, "style");
        b.AddContent(_seq++, Css);
        b.CloseElement();

        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-container");

        b.OpenElement(_seq++, "div"); b.AddAttribute(_seq++, "class", "ls-border-glow"); b.CloseElement();
        b.OpenElement(_seq++, "div"); b.AddAttribute(_seq++, "class", "ls-frame"); b.CloseElement();

        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-particles");
        // 24 颗浮游金尘：位置/延迟/时长/大小错开
        double[] pLeft = { 3, 7, 12, 18, 24, 30, 36, 42, 48, 54, 60, 66, 72, 78, 84, 90, 96, 9, 21, 45, 63, 81, 15, 88 };
        double[] pDelay = { 0, 0.4, 0.9, 1.3, 1.8, 2.2, 2.7, 3.1, 0.2, 0.7, 1.5, 2.0, 2.5, 3.4, 0.5, 1.1, 1.9, 2.9, 3.6, 0.8, 1.6, 2.4, 3.2, 3.8 };
        double[] pDur = { 6.5, 7.2, 8.0, 9.0, 7.5, 10.0, 8.5, 6.8, 9.5, 7.8, 11.0, 8.2, 9.2, 7.0, 10.5, 8.8, 6.2, 9.8, 7.4, 10.2, 8.6, 11.5, 7.6, 9.4 };
        string[] pSize = { "ls-particle-sm", "ls-particle-md", "ls-particle-lg", "ls-particle-sm", "ls-particle-xl", "ls-particle-md", "ls-particle-sm", "ls-particle-lg", "ls-particle-md", "ls-particle-sm", "ls-particle-xl", "ls-particle-md", "ls-particle-lg", "ls-particle-sm", "ls-particle-md", "ls-particle-sm", "ls-particle-lg", "ls-particle-md", "ls-particle-xl", "ls-particle-sm", "ls-particle-md", "ls-particle-lg", "ls-particle-sm", "ls-particle-md" };
        for (int pi = 0; pi < 24; pi++)
        {
            b.OpenElement(_seq++, "span");
            b.AddAttribute(_seq++, "class", $"ls-particle {pSize[pi]}");
            b.AddAttribute(_seq++, "style",
                $"left:{pLeft[pi].ToString(System.Globalization.CultureInfo.InvariantCulture)}%;" +
                $"animation-delay:{pDelay[pi].ToString(System.Globalization.CultureInfo.InvariantCulture)}s,{ (pDelay[pi] * 0.3).ToString(System.Globalization.CultureInfo.InvariantCulture)}s;" +
                $"animation-duration:{pDur[pi].ToString(System.Globalization.CultureInfo.InvariantCulture)}s,{(1.6 + pi % 5 * 0.25).ToString(System.Globalization.CultureInfo.InvariantCulture)}s;");
            b.CloseElement();
        }
        b.CloseElement();

        b.OpenElement(_seq++, "span"); b.AddAttribute(_seq++, "class", "ls-rune ls-rune-1"); b.AddContent(_seq++, "☯"); b.CloseElement();
        b.OpenElement(_seq++, "span"); b.AddAttribute(_seq++, "class", "ls-rune ls-rune-2"); b.AddContent(_seq++, "✦"); b.CloseElement();
        b.OpenElement(_seq++, "span"); b.AddAttribute(_seq++, "class", "ls-rune ls-rune-3"); b.AddContent(_seq++, "◈"); b.CloseElement();
        b.OpenElement(_seq++, "span"); b.AddAttribute(_seq++, "class", "ls-rune ls-rune-4"); b.AddContent(_seq++, "✧"); b.CloseElement();

        // ornate corners with gems
        foreach (var corner in new[] { "tl", "tr", "bl", "br" })
        {
            b.OpenElement(_seq++, "div");
            b.AddAttribute(_seq++, "class", $"ls-corner ls-corner-{corner}");
            b.OpenElement(_seq++, "span");
            b.AddAttribute(_seq++, "class", "ls-corner-gem");
            b.CloseElement();
            b.CloseElement();
        }

        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-inner");
        Panel(b);
        b.CloseElement();

        b.CloseElement();
    }

    void Panel(RenderTreeBuilder b)
    {
        // 标题 / 圣徽
        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-title-wrap");

        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-crest");
        b.OpenElement(_seq++, "div"); b.AddAttribute(_seq++, "class", "ls-crest-halo"); b.CloseElement();
        b.OpenElement(_seq++, "div"); b.AddAttribute(_seq++, "class", "ls-crest-ring"); b.CloseElement();
        b.OpenElement(_seq++, "div"); b.AddAttribute(_seq++, "class", "ls-crest-ring-inner"); b.CloseElement();
        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-crest-core");
        b.AddContent(_seq++, "枢");
        b.CloseElement();
        b.CloseElement();

        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-title-ornament");
        b.AddContent(_seq++, "◈  灵  枢  ◈");
        b.CloseElement();

        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-title");
        b.AddContent(_seq++, "灵枢 · OpenAI语言模型报错自动切换");
        b.CloseElement();

        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-title-sub");
        b.AddContent(_seq++, "—  通  道  ·  容  灾  ·  思  维  —");
        b.CloseElement();

        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-title-bar");
        b.CloseElement();
        b.CloseElement();

        // 特性条（装饰，非业务文案）
        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-feature-strip");
        FeatureChip(b, "多路圣渠");
        FeatureChip(b, "自动容灾");
        FeatureChip(b, "思维链");
        FeatureChip(b, "一语切换");
        FeatureChip(b, "模型探测");
        b.CloseElement();

        // 说明（圣谕碑）
        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-alert");

        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-alert-header");
        b.OpenElement(_seq++, "span");
        b.AddAttribute(_seq++, "class", "ls-alert-header-icon");
        b.AddContent(_seq++, "✦");
        b.CloseElement();
        b.OpenElement(_seq++, "span");
        b.AddAttribute(_seq++, "class", "ls-alert-header-title");
        b.AddContent(_seq++, "灵枢圣谕");
        b.CloseElement();
        b.OpenElement(_seq++, "span");
        b.AddAttribute(_seq++, "class", "ls-alert-header-line");
        b.CloseElement();
        b.CloseElement();

        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-alert-body");
        AlertLine(b, "✧", "替换框架内置 OpenAI 语言模型，支持多路文本模型自动容灾切换、拖动排序与自由增删渠道组", false);
        AlertLine(b, "✧", "遇到 HTTP 429/402/5xx 错误或响应体包含指定关键字时，自动切换到下一组渠道重试", false);
        AlertLine(b, "✧", "同时支持 reasoning_content 等 SSE 思维链流的自动转换", false);
        AlertLine(b, "◈", "使用前请在角色配置中禁用 OpenAILanguageModel，启用本模块", true);
        b.CloseElement();

        b.CloseElement();

        // === 安全设置（API Key 加密开关）===
        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-section");
        SectionTitle(b, "安全设置");
        AddEncryptionToggle(b);
        AddSslToggle(b);
        b.CloseElement();

        // === 渠道组（可增删，拖动排序，最上方为主组）===
        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-groups");
        AddHint(b, "拖动卡片调整渠道顺序：拖到最上方的组即为主渠道（优先使用），容灾按从上到下的顺序依次尝试。可点击「添加渠道」新增组、展开卡片后删除组。");
        Configuration.EnsureGroups();
        EnsureDetectArrays();
        int[] order = LanguageModelRouter.GetGroupOrder(Configuration);
        for (int di = 0; di < order.Length; di++)
            GroupCard(b, di);

        // 添加渠道组
        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-add-row");
        var canAdd = Configuration.Groups.Count < LanguageModelRouterConfig.MaxGroups;
        b.OpenElement(_seq++, "button");
        b.AddAttribute(_seq++, "type", "button");
        b.AddAttribute(_seq++, "class", "ls-btn-add");
        if (!canAdd)
            b.AddAttribute(_seq++, "disabled", true);
        b.AddAttribute(_seq++, "onclick", EventCallback.Factory.Create(this, () =>
        {
            Configuration.AddGroup();
            EnsureDetectArrays();
            StateHasChanged();
        }));
        b.AddContent(_seq++, canAdd
            ? $"+ 添加渠道组（当前 {Configuration.Groups.Count}/{LanguageModelRouterConfig.MaxGroups}）"
            : $"已达上限 {LanguageModelRouterConfig.MaxGroups} 组");
        b.CloseElement();
        b.CloseElement();
        b.CloseElement();

        // 圣印分隔
        SealDivider(b);


        // === 容灾设置 ===
        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-section");
        SectionTitle(b, "容灾设置");

        AutoFailoverToggle(b);

        AddInput(b, "错误关键字（逗号分隔）", Configuration.ErrorKeywords ?? "", v => Configuration.ErrorKeywords = string.IsNullOrEmpty(v) ? null : v);
        AddHint(b, "响应体中包含这些关键字时触发切换，如 rate_limit,insufficient_quota,billing_hard_limit\n留空则仅按 HTTP 状态码判断；内容安全检查类错误（如 data_inspection_failed、content_filter）已内置自动容灾");

        AddInput(b, "重试间隔（毫秒）", Configuration.RetryDelayMs.ToString(), v =>
        {
            if (int.TryParse(v, out var n))
                Configuration.RetryDelayMs = Math.Clamp(n, 0, 30000);
        });
        AddHint(b, "切换到下一组前的等待时间，默认 1000ms，设为 0 则立即重试");

        AddInput(b, "请求超时（毫秒）", Configuration.RequestTimeoutMs.ToString(), v =>
        {
            if (int.TryParse(v, out var n))
                Configuration.RequestTimeoutMs = Math.Clamp(n, 1000, 300000);
        });
        AddHint(b, "单次渠道请求超时上限，默认 30000ms。渠道挂起/无响应时按此时间快速容灾切换，避免长时间卡住；设为 0 则保留 100 秒兜底");

        AddInput(b, "流空闲超时（毫秒）", Configuration.StreamIdleTimeoutMs.ToString(), v =>
        {
            if (int.TryParse(v, out var n))
                Configuration.StreamIdleTimeoutMs = Math.Clamp(n, 0, 600000);
        });
        AddHint(b, "已建立连接但长时间未收到任何数据（含 keep-alive）视为异常，默认 0=关闭。防止渠道返回 200 后流挂起导致永久等待；模型思考较久时可能误判，请酌情设置");

        b.CloseElement();

        // === 手动切换 ===
        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-section");
        SectionTitle(b, "手动切换");

        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-status-row");
        b.AddContent(_seq++, $"当前状态：{GetActiveGroupLabel()}");
        b.OpenElement(_seq++, "span");
        b.AddAttribute(_seq++, "class", Configuration.ForcedGroupIndex < 0 ? "ls-badge ls-badge-on" : "ls-badge ls-badge-active");
        b.AddContent(_seq++, Configuration.ForcedGroupIndex < 0 ? "自动容灾" : "强制锁定");
        b.CloseElement();
        b.CloseElement();

        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-btn-row");

        int forcedIdx = Configuration.ForcedGroupIndex;
        for (int di = 0; di < order.Length; di++)
        {
            int group = order[di];
            string name = LanguageModelRouter.GetGroupName(group, Configuration);
            string mainMark = di == 0 ? " ★主" : "";
            string btnLabel = string.IsNullOrWhiteSpace(name) ? $"第{group + 1}组{mainMark}" : $"第{group + 1}组({name}){mainMark}";
            bool configured = IsGroupConfigured(group);
            AddSwitchBtn(b, btnLabel, forcedIdx == group, configured, () => SwitchTo(group));
        }
        // 返回自动容灾
        AddSwitchBtn(b, "自动容灾", forcedIdx < 0, true, () => SwitchTo(-1));

        b.CloseElement();
        AddHint(b, "点击按钮切换渠道，也可在聊天中告诉桌宠「切换到第二组」或按名称「切换到 deepseek」，AI 会自动切换。配置保存后即刻生效，无需重新加载模块。");
        b.CloseElement();

        // === 使用说明 ===
        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-section");
        SectionTitle(b, "使用说明");
        AddHint(b, "1. 在角色配置中禁用「OpenAI语言模型」，启用「灵枢 - OpenAI语言模型报错自动切换」\n2. 拖动渠道卡片可调整顺序：拖到最上方的组即为主渠道，必须填写 Endpoint、Model ID 和 API Key\n3. 其余组为备用渠道，遇到 429/402/5xx 错误时按从上到下顺序自动切换\n4. 组名称可用于 AI 识别渠道，如对桌宠说「切换到 deepseek」即可对应切换\n5. 可点击「添加渠道组」自由新增，展开备用组卡片后点「删除」移除（主组不可删，至少保留 1 组）\n6. 配置保存后即刻生效，无需重新加载模块");
        b.CloseElement();
    }

    void FeatureChip(RenderTreeBuilder b, string text)
    {
        b.OpenElement(_seq++, "span");
        b.AddAttribute(_seq++, "class", "ls-feature-chip");
        b.AddContent(_seq++, text);
        b.CloseElement();
    }

    void AlertLine(RenderTreeBuilder b, string mark, string text, bool warn)
    {
        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", warn ? "ls-alert-line ls-alert-line-warn" : "ls-alert-line");
        b.OpenElement(_seq++, "span");
        b.AddAttribute(_seq++, "class", "ls-alert-mark");
        b.AddContent(_seq++, mark);
        b.CloseElement();
        b.OpenElement(_seq++, "span");
        b.AddAttribute(_seq++, "class", "ls-alert-text");
        b.AddContent(_seq++, text);
        b.CloseElement();
        b.CloseElement();
    }

    void SealDivider(RenderTreeBuilder b)
    {
        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-seal");
        b.OpenElement(_seq++, "span");
        b.AddAttribute(_seq++, "class", "ls-seal-icon");
        b.AddContent(_seq++, "✦");
        b.CloseElement();
        b.CloseElement();
    }

    /// <summary>确保探测结果数组与当前组数对齐（增删组后调用）</summary>
    void EnsureDetectArrays()
    {
        int n = Configuration!.Groups.Count;
        if (_detectResults.Length != n)
        {
            var dr = new string?[n];
            var dm = new List<string>?[n];
            int copy = Math.Min(n, _detectResults.Length);
            Array.Copy(_detectResults, dr, copy);
            Array.Copy(_detectedModels, dm, copy);
            _detectResults = dr;
            _detectedModels = dm;
        }
    }

    bool IsGroupConfigured(int g)
    {
        var cfg = Configuration!;
        cfg.EnsureGroups();
        if (g < 0 || g >= cfg.Groups.Count) return false;
        return cfg.Groups[g].IsConfigured;
    }

    // ==================== Group Config ====================

    void GroupConfig(RenderTreeBuilder b, int g, bool isPrimary)
    {
        var cfg = Configuration!;
        cfg.EnsureGroups();
        if (g < 0 || g >= cfg.Groups.Count) return;
        var ch = cfg.Groups[g];

        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-section");
        SectionTitle(b, isPrimary ? $"主渠道（第{g + 1}组，必填）" : $"第{g + 1}组配置");

        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-status-row");
        b.OpenElement(_seq++, "span");
        b.AddAttribute(_seq++, "class", IsGroupConfigured(g) ? "ls-badge ls-badge-on" : "ls-badge ls-badge-off");
        b.AddContent(_seq++, IsGroupConfigured(g) ? "✦ 已配置" : "○ 未配置");
        b.CloseElement();
        b.CloseElement();

        AddInput(b, "组名称（可选，供 AI 识别）", ch.GroupName ?? "", v => ch.GroupName = v);
        AddInput(b, "Endpoint", ch.Endpoint ?? "", v => ch.Endpoint = v);
        AddHint(b, "API 端点 URL，如 https://api.openai.com/v1");
        AddInput(b, "Model ID", ch.ModelId ?? "", v => ch.ModelId = v);
        AddHint(b, "模型标识，如 gpt-4o、deepseek-chat");

        ProbeSection(b, g);

        // API Key：加密开关开启时以 DPAPI 密文存储（显示解密、输入加密）；关闭时与官方插件一致明文保存。
        // GroupConfig 开头的 EnsureGroups 已按开关统一转换存储形态，UnprotectSecret 对明文原样返回，读取恒安全。
        AddPassword(b, "API Key", LanguageModelRouterConfig.UnprotectSecret(ch.ApiKey),
            v => ch.ApiKey = cfg.EncryptApiKeys
                ? (LanguageModelRouterConfig.ProtectSecret(v) ?? "")
                : v);
        AddHint(b, cfg.EncryptApiKeys
            ? "已使用 Windows DPAPI 加密存储，仅当前系统用户可解密"
            : "明文保存于配置文件（与官方语言模型插件一致），请注意保护配置文件");

        AddInput(b, "Reasoning Effort", ch.ReasoningEffort ?? "", v => ch.ReasoningEffort = string.IsNullOrWhiteSpace(v) ? null : v);
        AddHint(b, "推理强度，如 low / medium / high，留空则不设置");
        AddInput(b, "Extra Headers (JSON)", ch.ExtraHeaders ?? "", v => ch.ExtraHeaders = string.IsNullOrWhiteSpace(v) ? null : v);
        AddHint(b, "额外请求头，JSON 格式，如 {\"X-Custom\":\"value\"}");
        AddInput(b, "Extra Body (JSON)", ch.ExtraBody ?? "", v => ch.ExtraBody = string.IsNullOrWhiteSpace(v) ? null : v);
        AddHint(b, "额外请求体，JSON 格式，如 {\"temperature\":0.7}");

        AddInput(b, "Extra Body NotThinking (JSON)", ch.ExtraBodyNotThinking ?? "", v => ch.ExtraBodyNotThinking = string.IsNullOrWhiteSpace(v) ? null : v);
        AddHint(b, "非思考模式的额外请求体，JSON 格式；留空则默认发送 {\"thinking\":{\"type\":\"disabled\"}}，明确要求模型关闭思考");

        b.CloseElement();
    }

    // ==================== 拖动排序 ====================

    /// <summary>渲染一张可拖动的渠道组卡片（displayIndex 为显示位置，0 = 主组）。主组与备用组均可折叠。</summary>
    void GroupCard(RenderTreeBuilder b, int displayIndex)
    {
        int slot = GetDisplaySlot(displayIndex);
        bool isPrimary = displayIndex == 0;
        bool isCurrent = IsCurrentSlot(slot);

        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", _dragSource == displayIndex ? "ls-group-card ls-group-card-dragging" : "ls-group-card");
        b.AddAttribute(_seq++, "draggable", "true");
        b.AddAttribute(_seq++, "ondragstart", EventCallback.Factory.Create<DragEventArgs>(this, e => _dragSource = displayIndex));
        b.AddAttribute(_seq++, "ondragend", EventCallback.Factory.Create<DragEventArgs>(this, e => { _dragSource = null; StateHasChanged(); }));
        b.AddAttribute(_seq++, "ondragover", EventCallback.Factory.Create<DragEventArgs>(this, e => { }));
        b.AddEventPreventDefaultAttribute(_seq++, "ondragover", true);
        b.AddAttribute(_seq++, "ondrop", EventCallback.Factory.Create<DragEventArgs>(this, e => DropGroup(displayIndex)));
        b.AddEventPreventDefaultAttribute(_seq++, "ondrop", true);

        // 卡片头：拖拽手柄 + 标题 + 徽标 + 删除按钮
        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-group-card-head");

        b.OpenElement(_seq++, "span");
        b.AddAttribute(_seq++, "class", "ls-drag-handle");
        b.AddContent(_seq++, "⠿");
        b.CloseElement();

        b.OpenElement(_seq++, "span");
        b.AddAttribute(_seq++, "class", "ls-group-card-title");
        b.AddContent(_seq++, GetGroupCardTitle(slot));
        b.CloseElement();

        if (isPrimary)
        {
            b.OpenElement(_seq++, "span");
            b.AddAttribute(_seq++, "class", "ls-badge ls-badge-main");
            b.AddContent(_seq++, "★ 主组");
            b.CloseElement();
        }
        if (isCurrent)
        {
            b.OpenElement(_seq++, "span");
            b.AddAttribute(_seq++, "class", "ls-badge ls-badge-active");
            b.AddContent(_seq++, "当前通道");
            b.CloseElement();
        }
        b.CloseElement();

        // 卡片内容：主组与备用组均使用可折叠卡片（主组默认展开，备用组可删除）
        AddCollapsibleGroup(b, isPrimary ? $"主渠道（第{slot + 1}组，必填）" : "展开配置", isPrimary, () => GroupConfig(b, slot, isPrimary), canRemove: !isPrimary, onRemove: () =>
        {
            Configuration!.RemoveGroup(slot);
            EnsureDetectArrays();
            StateHasChanged();
        });

        b.CloseElement();
    }

    /// <summary>拖放：交换两组的显示顺序（拖到最上方即成为主组），同时修正强制锁定索引</summary>
    void DropGroup(int targetDisplayIndex)
    {
        if (_dragSource == null || _dragSource.Value == targetDisplayIndex)
        {
            _dragSource = null;
            return;
        }

        var groups = Configuration!.Groups;
        int src = _dragSource.Value;
        (groups[src], groups[targetDisplayIndex]) = (groups[targetDisplayIndex], groups[src]);

        // 拖动排序后强制锁定跟随原组移动
        int forced = Configuration.ForcedGroupIndex;
        if (forced == src)
            Configuration.ForcedGroupIndex = targetDisplayIndex;
        else if (forced == targetDisplayIndex)
            Configuration.ForcedGroupIndex = src;

        _dragSource = null;
        EnsureDetectArrays();
        StateHasChanged();
    }

    /// <summary>显示位置 → Groups 索引（显示位置即索引，列表顺序即顺序）</summary>
    int GetDisplaySlot(int displayIndex)
    {
        var order = LanguageModelRouter.GetGroupOrder(Configuration);
        if (displayIndex < 0 || displayIndex >= order.Length) return displayIndex;
        return order[displayIndex];
    }

    /// <summary>卡片标题：第 N 组（名称）</summary>
    string GetGroupCardTitle(int slot)
    {
        string name = LanguageModelRouter.GetGroupName(slot, Configuration);
        string baseTitle = $"第{slot + 1}组";
        return string.IsNullOrWhiteSpace(name) ? baseTitle : $"{baseTitle}（{name}）";
    }

    /// <summary>槽位是否为当前生效渠道（强制锁定优先，否则主组）</summary>
    bool IsCurrentSlot(int slot)
    {
        var cfg = Configuration!;
        if (cfg.ForcedGroupIndex >= 0) return cfg.ForcedGroupIndex == slot;
        return LanguageModelRouter.GetPrimarySlot(cfg) == slot;
    }

    // ==================== Probe & Dropdown ====================

    void ProbeSection(RenderTreeBuilder b, int groupIndex)
    {
        EnsureDetectArrays();
        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-probe");

        b.OpenElement(_seq++, "button");
        b.AddAttribute(_seq++, "type", "button");
        b.AddAttribute(_seq++, "class", "ls-btn-probe");
        b.AddAttribute(_seq++, "onclick", EventCallback.Factory.Create(this, async () =>
        {
            await ProbeGroup(groupIndex);
        }));
        b.OpenElement(_seq++, "span");
        b.AddAttribute(_seq++, "class", "ls-btn-probe-icon");
        b.AddContent(_seq++, "✧");
        b.CloseElement();
        b.AddContent(_seq++, $"探测第{groupIndex + 1}组");
        b.CloseElement();

        var result = _detectResults[groupIndex];
        if (result != null)
        {
            b.OpenElement(_seq++, "span");
            bool ok = result.Contains("连接成功") || result.Contains("个模型");
            b.AddAttribute(_seq++, "class", ok ? "ls-probe-result ls-probe-ok" : "ls-probe-result ls-probe-fail");
            b.AddContent(_seq++, result);
            b.CloseElement();
        }

        var models = _detectedModels[groupIndex];
        if (models != null && models.Count > 0)
        {
            b.OpenElement(_seq++, "div");
            b.AddAttribute(_seq++, "class", "ls-probe-select-row");

            b.OpenElement(_seq++, "span");
            b.AddContent(_seq++, "选择模型：");
            b.CloseElement();

            b.OpenElement(_seq++, "select");
            b.AddAttribute(_seq++, "class", "ls-select");
            b.AddAttribute(_seq++, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, e =>
            {
                if (e.Value is string val && !string.IsNullOrWhiteSpace(val)
                    && groupIndex >= 0 && groupIndex < Configuration!.Groups.Count)
                {
                    Configuration.Groups[groupIndex].ModelId = val;
                    StateHasChanged();
                }
            }));
            b.OpenElement(_seq++, "option");
            b.AddAttribute(_seq++, "value", "");
            Configuration!.EnsureGroups();
            string cur = (groupIndex >= 0 && groupIndex < Configuration.Groups.Count)
                ? Configuration.Groups[groupIndex].ModelId ?? ""
                : "";
            b.AddContent(_seq++, $"— 当前: {(string.IsNullOrWhiteSpace(cur) ? "未设置" : cur)} —");
            b.CloseElement();
            foreach (var m in models)
            {
                b.OpenElement(_seq++, "option");
                b.AddAttribute(_seq++, "value", m);
                b.AddContent(_seq++, m);
                b.CloseElement();
            }
            b.CloseElement();

            b.CloseElement();
        }

        b.CloseElement();
    }

    async Task ProbeGroup(int groupIndex)
    {
        EnsureDetectArrays();
        if (groupIndex < 0 || groupIndex >= _detectResults.Length) return;

        _detectResults[groupIndex] = "探测中…";
        _detectedModels[groupIndex] = null;
        StateHasChanged();

        var (display, models) = await LanguageModelRouter.FetchModels(groupIndex, Configuration!);
        _detectResults[groupIndex] = display;
        _detectedModels[groupIndex] = models;
        StateHasChanged();
    }

    // ==================== API Key 加密开关 ====================

    void AddEncryptionToggle(RenderTreeBuilder b)
    {
        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-toggle-row");

        b.OpenElement(_seq++, "input");
        b.AddAttribute(_seq++, "type", "checkbox");
        b.AddAttribute(_seq++, "id", "encryptKeyCheck");
        b.AddAttribute(_seq++, "checked", Configuration.EncryptApiKeys);
        b.AddAttribute(_seq++, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, e =>
        {
            bool next = e.Value is bool bv ? bv : !Configuration.EncryptApiKeys;
            Configuration.EncryptApiKeys = next;
            // 立即按新开关状态统一转换所有 Key（开启→密文，关闭→明文），随配置保存生效
            Configuration.EnsureApiKeysProtected();
            StateHasChanged();
        }));
        b.CloseElement();

        b.OpenElement(_seq++, "label");
        b.AddAttribute(_seq++, "for", "encryptKeyCheck");
        b.AddContent(_seq++, "加密 API Key 存储（开启=Windows DPAPI 密文保存，仅当前系统用户可解密；关闭=明文保存于配置文件，与官方插件一致）");
        b.CloseElement();
        b.CloseElement();

        AddHint(b, "切换后立即转换当前所有渠道的 Key 并随配置保存。明文模式下 Key 直接可见，便于跨机器迁移；开启加密后仅当前 Windows 用户可解密。");
    }

    void AddSslToggle(RenderTreeBuilder b)
    {
        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-toggle-row");

        b.OpenElement(_seq++, "input");
        b.AddAttribute(_seq++, "type", "checkbox");
        b.AddAttribute(_seq++, "id", "ignoreSslCheck");
        b.AddAttribute(_seq++, "checked", Configuration.IgnoreSslCertificate);
        b.AddAttribute(_seq++, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, e =>
        {
            Configuration.IgnoreSslCertificate = e.Value is bool bv ? bv : !Configuration.IgnoreSslCertificate;
            StateHasChanged();
        }));
        b.CloseElement();

        b.OpenElement(_seq++, "label");
        b.AddAttribute(_seq++, "for", "ignoreSslCheck");
        b.AddContent(_seq++, "忽略 TLS 证书校验错误（默认开启，兼容自签名证书/证书异常的渠道）");
        b.CloseElement();
        b.CloseElement();

        AddHint(b, "关闭后走系统证书校验（更安全，可防中间人攻击）；使用自签名证书或证书异常的渠道将无法连接，请在能正常访问的前提下尽量关闭。");
    }

    // ==================== Auto Failover Toggle ====================

    void AutoFailoverToggle(RenderTreeBuilder b)
    {
        // 自动容灾
        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-toggle-row");

        b.OpenElement(_seq++, "input");
        b.AddAttribute(_seq++, "type", "checkbox");
        b.AddAttribute(_seq++, "id", "autoFailoverCheck");
        b.AddAttribute(_seq++, "checked", Configuration.AutoFailoverEnabled);
        b.AddAttribute(_seq++, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, e =>
        {
            Configuration.AutoFailoverEnabled = e.Value is bool bv ? bv : !Configuration.AutoFailoverEnabled;
            StateHasChanged();
        }));
        b.CloseElement();

        b.OpenElement(_seq++, "label");
        b.AddAttribute(_seq++, "for", "autoFailoverCheck");
        b.AddContent(_seq++, "启用自动容灾（开启后无论切换到哪个组，遇错误时自动切换备用渠道）");
        b.CloseElement();
        b.CloseElement();

        // 优先主渠道
        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-toggle-row");

        b.OpenElement(_seq++, "input");
        b.AddAttribute(_seq++, "type", "checkbox");
        b.AddAttribute(_seq++, "id", "priorityMainCheck");
        b.AddAttribute(_seq++, "checked", Configuration.PriorityMainChannel);
        b.AddAttribute(_seq++, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, e =>
        {
            Configuration.PriorityMainChannel = e.Value is bool bv ? bv : !Configuration.PriorityMainChannel;
            StateHasChanged();
        }));
        b.CloseElement();

        b.OpenElement(_seq++, "label");
        b.AddAttribute(_seq++, "for", "priorityMainCheck");
        b.AddContent(_seq++, "优先主渠道（每次对话优先尝试主渠道，容灾全程静默，仅日志可见）");
        b.CloseElement();
        b.CloseElement();

        // 思维链显示
        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-toggle-row");

        b.OpenElement(_seq++, "input");
        b.AddAttribute(_seq++, "type", "checkbox");
        b.AddAttribute(_seq++, "id", "showThinkingCheck");
        b.AddAttribute(_seq++, "checked", Configuration.ShowThinkingChain);
        b.AddAttribute(_seq++, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, e =>
        {
            Configuration.ShowThinkingChain = e.Value is bool bv ? bv : !Configuration.ShowThinkingChain;
            StateHasChanged();
        }));
        b.CloseElement();

        b.OpenElement(_seq++, "label");
        b.AddAttribute(_seq++, "for", "showThinkingCheck");
        b.AddContent(_seq++, "显示思维链（开启后将 reasoning/thinking 等字段转为可见内容，关闭则隐藏；实时生效）");
        b.CloseElement();
        b.CloseElement();

        // 智能思考/非思考切换
        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-toggle-row");

        b.OpenElement(_seq++, "input");
        b.AddAttribute(_seq++, "type", "checkbox");
        b.AddAttribute(_seq++, "id", "smartThinkingCheck");
        b.AddAttribute(_seq++, "checked", Configuration.SmartThinkingEnabled);
        b.AddAttribute(_seq++, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, e =>
        {
            Configuration.SmartThinkingEnabled = e.Value is bool bv ? bv : !Configuration.SmartThinkingEnabled;
            StateHasChanged();
        }));
        b.CloseElement();

        b.OpenElement(_seq++, "label");
        b.AddAttribute(_seq++, "for", "smartThinkingCheck");
        b.AddContent(_seq++, "智能思考/非思考切换（开启后默认非思考：回复更快、token 更省；AI 需调用工具等复杂任务时自动切回思考）");
        b.CloseElement();
        b.CloseElement();

        // 忽略历史隐式功能占用
        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-toggle-row");

        b.OpenElement(_seq++, "input");
        b.AddAttribute(_seq++, "type", "checkbox");
        b.AddAttribute(_seq++, "id", "ignorePersistentThinkingCheck");
        b.AddAttribute(_seq++, "checked", Configuration.IgnorePersistentThinking);
        b.AddAttribute(_seq++, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, e =>
        {
            Configuration.IgnorePersistentThinking = e.Value is bool bv ? bv : !Configuration.IgnorePersistentThinking;
            StateHasChanged();
        }));
        b.CloseElement();

        b.OpenElement(_seq++, "label");
        b.AddAttribute(_seq++, "for", "ignorePersistentThinkingCheck");
        b.AddContent(_seq++, "忽略历史隐式功能占用（针对历史中调用过工具的角色：日常闲聊走非思考更省 token，AI 即将调用工具/函数出错时仍自动切回思考）");
        b.CloseElement();
        b.CloseElement();

        AddInput(b, "思考触发关键词（逗号分隔）", Configuration.ThinkingTriggerKeywords ?? "", v => Configuration.ThinkingTriggerKeywords = string.IsNullOrWhiteSpace(v) ? null : v);
        AddHint(b, "用户消息命中任一关键词时强制走思考模式（即使默认非思考也临时切回），适合需要深度推理的场景，如：代码,数学,分析");
    }

    // ==================== Switch ====================

    void SwitchTo(int groupIndex)
    {
        // -1 = 自动容灾；其他索引必须指向已配置组，否则不生效（避免 UI 显示锁定但实际走主组的假象）
        if (groupIndex >= 0 && !IsGroupConfigured(groupIndex))
        {
            // 提示并保持现状
            return;
        }
        Configuration.ForcedGroupIndex = groupIndex;
        LanguageModelRouter.OnGroupChanged?.Invoke();
        StateHasChanged();
    }

    void AddSwitchBtn(RenderTreeBuilder b, string text, bool active, bool configured, Action onClick)
    {
        b.OpenElement(_seq++, "button");
        b.AddAttribute(_seq++, "type", "button");
        string cls = active ? "ls-btn ls-btn-active" : "ls-btn";
        if (!configured && !active) cls += " ls-btn-dim";
        b.AddAttribute(_seq++, "class", cls);
        b.AddAttribute(_seq++, "onclick", EventCallback.Factory.Create(this, onClick));
        b.AddContent(_seq++, text);
        b.CloseElement();
    }

    // ==================== Shared Helpers ====================

    string GetActiveGroupLabel()
    {
        int idx = Configuration.ForcedGroupIndex;
        if (idx < 0) return "自动容灾（主组优先）";
        if (idx >= Configuration.Groups.Count || !Configuration.Groups[idx].IsConfigured)
            return "自动容灾（当前锁定组未配置）";
        return LanguageModelRouter.GetGroupLabel(idx, Configuration);
    }


    void SectionTitle(RenderTreeBuilder b, string text)
    {
        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-section-title");
        b.AddContent(_seq++, text);
        b.CloseElement();
    }

    void AddHint(RenderTreeBuilder b, string text)
    {
        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-hint");
        b.AddContent(_seq++, text);
        b.CloseElement();
    }

    void AddLabel(RenderTreeBuilder b, string text)
    {
        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-label");
        b.AddContent(_seq++, text);
        b.CloseElement();
    }

    void AddInput(RenderTreeBuilder b, string label, string value, Action<string> setter)
    {
        AddLabel(b, label);
        b.OpenComponent<Input<string>>(_seq++);
        b.AddAttribute(_seq++, "Value", value);
        b.AddAttribute(_seq++, "ValueChanged",
            EventCallback.Factory.Create<string>(this, setter));
        b.CloseComponent();
    }

    void AddPassword(RenderTreeBuilder b, string label, string value, Action<string> setter)
    {
        AddLabel(b, label);
        b.OpenComponent<InputPassword>(_seq++);
        b.AddAttribute(_seq++, "Value", value);
        b.AddAttribute(_seq++, "ValueChanged",
            EventCallback.Factory.Create<string>(this, setter));
        b.CloseComponent();
    }

    void AddCollapsibleGroup(RenderTreeBuilder b, string title, bool defaultOpen, Action renderContent, bool canRemove = false, Action? onRemove = null)
    {
        b.OpenElement(_seq++, "details");
        b.AddAttribute(_seq++, "class", "ls-details");
        if (defaultOpen)
            b.AddAttribute(_seq++, "open", true);

        b.OpenElement(_seq++, "summary");
        b.OpenElement(_seq++, "span");
        b.AddAttribute(_seq++, "class", "ls-arrow");
        b.AddContent(_seq++, "▶");
        b.CloseElement();
        b.AddContent(_seq++, " " + title);

        if (canRemove && onRemove != null)
        {
            b.OpenElement(_seq++, "button");
            b.AddAttribute(_seq++, "type", "button");
            b.AddAttribute(_seq++, "class", "ls-btn-remove");
            b.AddAttribute(_seq++, "style", "margin-left:8px; padding:1px 10px; font-size:11px;");
            b.AddAttribute(_seq++, "onclick", EventCallback.Factory.Create(this, (MouseEventArgs e) =>
            {
                onRemove();
            }));
            b.AddContent(_seq++, "删除");
            b.CloseElement();
        }

        b.CloseElement();

        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "class", "ls-details-content");
        renderContent();
        b.CloseElement();
        b.CloseElement();
    }
}
