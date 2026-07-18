using System;

namespace Guildmaster.Balance.Editor
{
    /// <summary>
    /// Оценка силы по парным исходам (Bradley-Terry, MM-итерации). Корректнее сырого win-rate: пулит
    /// информацию по парам. Регуляризация — виртуальная ничья каждого против «среднего» игрока силы 1 —
    /// держит оценки конечными при идеальной сепарации (кто-то выиграл/проиграл всё). Оговорка: сейчас
    /// бой RNG-free → исходы детерминированы (1/0), и рейтинг вырождается в топологический порядок; при
    /// нетранзитивности (камень-ножницы-бумага) MM всё равно сходится к разумному упорядочиванию.
    /// </summary>
    internal static class BradleyTerry
    {
        /// <param name="wins">wins[i][j] — сколько раз i победил j (ничья = 0.5 каждому).</param>
        /// <param name="games">games[i][j] — сколько игр между i и j.</param>
        /// <returns>Сила каждого (нормирована к геометрическому среднему 1). Больше = сильнее.</returns>
        public static double[] Fit(int n, double[][] wins, double[][] games, int iterations = 500)
        {
            var p = new double[n];
            for (int i = 0; i < n; i++) p[i] = 1.0;

            var totalWins = new double[n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    if (i != j) totalWins[i] += wins[i][j];

            const double reg = 0.5; // виртуальная пара против «среднего» (сила 1): половина игры, половина победы

            for (int iter = 0; iter < iterations; iter++)
            {
                var np = new double[n];
                for (int i = 0; i < n; i++)
                {
                    double denom = reg / (p[i] + 1.0);
                    for (int j = 0; j < n; j++)
                    {
                        if (i == j) continue;
                        double nij = games[i][j];
                        if (nij > 0) denom += nij / (p[i] + p[j]);
                    }
                    double num = totalWins[i] + reg * 0.5;
                    np[i] = num / denom;
                }

                double logSum = 0;
                for (int i = 0; i < n; i++) logSum += Math.Log(np[i]);
                double geoMean = Math.Exp(logSum / n);
                for (int i = 0; i < n; i++) p[i] = np[i] / geoMean;
            }

            return p;
        }
    }
}
