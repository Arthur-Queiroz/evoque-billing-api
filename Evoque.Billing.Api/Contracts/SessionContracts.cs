using System.ComponentModel.DataAnnotations;

namespace Evoque.Billing.Api.Contracts;

/// <summary>
/// O limite de tamanho não é capricho. Este endpoint é anônimo e alcançável da
/// internet: sem ele, cada tentativa pode mandar uma senha de megabytes, e o
/// serviço aloca um array do mesmo tamanho para comparar. Nenhuma senha real
/// chega perto de 256 caracteres.
/// </summary>
public sealed record SignInRequest(
    [Required, MaxLength(256)] string Username,
    [Required, MaxLength(256)] string Password);

public sealed record SessionResponse(string OperatorId);
