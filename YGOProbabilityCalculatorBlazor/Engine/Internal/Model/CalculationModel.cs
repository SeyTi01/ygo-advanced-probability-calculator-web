namespace CardProbability.Internal.Model;

// Never infer a mode from effect presence or a secondary certificate.
internal enum CalculationModel { Fast, OpeningHand, Exact }
