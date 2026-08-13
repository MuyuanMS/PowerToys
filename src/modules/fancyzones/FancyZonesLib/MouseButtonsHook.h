#pragma once

#include <functional>

class MouseButtonsHook
{
public:
    MouseButtonsHook(std::function<void()>, std::function<void()>, std::function<bool(bool)>, std::function<bool(bool)>);
    void enable();
    void disable();

private:
    static HHOOK hHook;
    static std::function<void()> middleClickCallback;
    static std::function<void()> secondaryClickCallback;
    static std::function<bool(bool)> wheelActiveCallback; // true reserves the full high-resolution gesture, including sub-detent packets
    static std::function<bool(bool)> wheelCallback; // gets wheel direction (true = up), returns true when a full-detent action is accepted
    static int wheelDeltaAccumulator;
    static LRESULT CALLBACK MouseButtonsProc(int, WPARAM, LPARAM);
};
