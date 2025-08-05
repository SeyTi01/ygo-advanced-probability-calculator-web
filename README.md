# Yu-Gi-Oh! Advanced Probability Calculator

A deck-building tool that calculates the **exact probability** of opening playable hands in Yu-Gi-Oh!, even for complex 2- and 3-card combo setups.  
Try it [here](https://ygo-calculator.pages.dev/).

## How It Works

1. **Create Categories**  
   Tag card roles like `Starter`, `Extender`, `X`, `CombosWithX`, etc.
2. **Assign Categories to Cards**  
   A card can belong to multiple categories.
3. **Define Combos**  
   Each combo is a combination of required categories (e.g., `Lubellion + Normal Summon`).
4. **Set Constraints**  
   Define per-category min/max requirements.
5. **Calculate**  
   Get exact probabilities for opening **any** of the defined combos in your deck.

## Example: Fiendsmith Bystial Deck

This deck can access its main combo by summoning any LIGHT Fiend monster to fulfill the requirements for "Fiendsmith Requiem".  
That can be accomplished by opening a one-card starter or by assembling a 2-card combo that can summon "Moon of the Closed Heaven" using any two Effect Monsters.  
The only way to summon additional effect monsters that are not LIGHT Fiends in this deck is by either using Bystials or Normal Summoning a hand trap.  
Bystials require a LIGHT or DARK monster in either GY to banish, and there are two ways this can be achieved on the first turn.  
If the Bystial was added by sending "The Bystial Lubellion" to the GY, it can always be summoned, so it combos with every normal summonable hand trap.  
However, if the Bystial was drawn normally, the normal summonable hand trap needs to be a LIGHT or DARK attribute and fulfill the requirements to summon "Salamangreat Almiraj", so it can be sent to the GY and used for the summon of the Bystial.

### 1. Category Definitions

- **1 Card Starter**: Any single card that independently initiates a Fiendsmith combo.
- **Bystial**: Any "Bystial" monster that can summon itself.
- **Lubellion**: Specifically "The Bystial Lubellion".
- **Normal Summon**: Any monster that can be Normal Summoned (e.g., Ash Blossom & Joyous Spring).
- **L/D Normal Summon**: Any LIGHT or DARK monster that can be Normal Summoned and sent to the GY by summoning "Salamangreat Almiraj" (e.g., Effect Veiler).

### 2. Combo Definitions

- **1-Card Combo**: Requires *any* card with the `1 Card Starter` tag.
- **Moon Combo 1**: Requires *both* the `Lubellion` tag *and* the `Normal Summon` tag in the opening hand.
- **Moon Combo 2**: Requires *both* the `Bystial` tag *and* the `L/D Normal Summon` tag in the opening hand.

After tagging all cards appropriately, this configuration will calculate the exact probability of opening a Fiendsmith play in the given deck list.

![Screenshot](YGOProbabilityCalculatorBlazor/Assets/readme_screenshot.png)

For further optimization, you could define categories like `Handtrap` or `Brick`, then require all of your combos to open at least one `Handtrap`-tagged card while opening zero `Brick`-tagged cards.  
This can help determine optimal deck sizes and engine ratios.

## Additional Features

- Import decks from `.ydk` files.
- Save and load sessions.

## Technical Stack

- Frontend: **Blazor WebAssembly** (.NET 9.0)
- Language: **C# 13.0**
