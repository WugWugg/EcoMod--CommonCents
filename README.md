# Description

**Detects pricing loops that let players profit from your store.**

CommonCents automatically identifies when your store’s prices allow players to buy low and sell high against you.

Get instant warnings with a clear breakdown of which items are causing losses and how much you’re losing per trade, so you can fix pricing issues before they cost you.

Mod.io - https://mod.io/g/eco/m/commoncents

# Architecture

```text
StoreItemData Event
        |
        v
Debounce Events (Drag-n-Drops create a lot of noise)
        |
        v
Find Arbitrage Opportunities in the Changed Store
        |
        v
Compare Hash of Arbitrages to Last Seen
        |
        +-- New State -------> Notify User of Arbitrages
        |
        \-- Matching State --> Silence Warning (User probably just reordered offers in the shop, not changing the prices)
```

# Problems

## Tags in Stores

## Debounce & Hash
Drag-n-Drop causes a lot of noise.
Simply moving items around, or adding an item that isn't an extreme price point needs to be filtered out early.
