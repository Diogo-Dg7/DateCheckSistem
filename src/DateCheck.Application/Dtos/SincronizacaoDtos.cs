namespace DateCheck.Application.Dtos;

/// <summary>
/// O que uma sincronização com uma origem externa fez no banco local. <paramref name="Atualizados"/> conta só os
/// itens que de fato mudaram (item recebido idêntico ao já importado não conta).
/// </summary>
public sealed record ResultadoSincronizacao(int Inseridos, int Atualizados, int Removidos)
{
    public static ResultadoSincronizacao Nenhum { get; } = new(0, 0, 0);

    public int Total => Inseridos + Atualizados + Removidos;
}
