namespace ChessGame.Api.Online;

public sealed record Page<T>(int PageNumber, int PageSize, long Total, List<T> Items);
