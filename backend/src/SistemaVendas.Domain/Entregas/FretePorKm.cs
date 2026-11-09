using SistemaVendas.Domain.Comum;

namespace SistemaVendas.Domain.Entregas;

/// <summary>
/// Frete pela distância de carro da loja até o CEP do cliente: valor base até alguns km
/// e um valor por km a partir daí. Ex.: até 2 km R$ 10; depois R$ 2/km; no máximo 20 km.
/// </summary>
public class FretePorKm : EntidadeDaLoja
{
    public bool Ativo { get; set; }

    /// <summary>CEP de onde sai a entrega (a loja), só os 8 dígitos.</summary>
    public int CepOrigem { get; set; }
    public double OrigemLat { get; set; }
    public double OrigemLon { get; set; }
    public string? OrigemEndereco { get; set; }

    public decimal KmInclusos { get; set; } = 2;
    public decimal ValorBase { get; set; } = 10;
    public decimal ValorPorKm { get; set; } = 2;
    public decimal KmMaximo { get; set; } = 20;

    /// <summary>Prazo em dias úteis (0 = no mesmo dia).</summary>
    public int Dias { get; set; } = 1;
}

/// <summary>Coordenada de um CEP já consultada: os serviços de mapa gratuitos são lentos e limitados.</summary>
public class LocalCep : Entidade
{
    public int Cep { get; set; }
    public double Lat { get; set; }
    public double Lon { get; set; }
    public required string Fonte { get; set; }

    /// <summary>Endereço que a BrasilAPI deu para o CEP, para o dono conferir se o ponto faz sentido.</summary>
    public string? Endereco { get; set; }
}

public static class RegrasFreteKm
{
    /// <summary>Quando não há rota de carro, a distância em linha reta vezes este fator (as ruas não são retas).</summary>
    public const double FatorRuas = 1.3;

    /// <summary>Distância em linha reta, em km.</summary>
    public static double LinhaReta(double lat1, double lon1, double lat2, double lon2)
    {
        const double raio = 6371.0;
        static double Rad(double g) => g * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return raio * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    /// <summary>Km arredondado para cima em uma casa (5,21 km vira 5,3 km), que é o que aparece para o cliente.</summary>
    public static decimal Arredondar(double km) => Math.Ceiling((decimal)km * 10) / 10;

    /// <summary>Valor do frete para a distância, ou null se passar do máximo atendido.</summary>
    public static decimal? Calcular(FretePorKm cfg, decimal km)
    {
        if (km > cfg.KmMaximo)
            return null;
        var excedente = Math.Max(0, km - cfg.KmInclusos);
        return Math.Round(cfg.ValorBase + excedente * cfg.ValorPorKm, 2, MidpointRounding.AwayFromZero);
    }

    public static string? Validar(decimal kmInclusos, decimal valorBase, decimal valorPorKm, decimal kmMaximo, int dias)
    {
        if (kmInclusos < 0 || valorBase < 0 || valorPorKm < 0)
            return "Os valores não podem ser negativos.";
        if (kmMaximo <= 0 || kmMaximo > 300)
            return "A distância máxima precisa ficar entre 1 e 300 km.";
        if (kmInclusos > kmMaximo)
            return "Os km da taxa base não podem passar da distância máxima.";
        if (dias is < 0 or > 60)
            return "O prazo precisa ficar entre 0 e 60 dias.";
        return null;
    }
}
