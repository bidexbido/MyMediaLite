// Copyright (C) 2010 Steffen Rendle, Zeno Gantner
// Copyright (C) 2011, 2012 Zeno Gantner
//
// This file is part of MyMediaLite.
//
// MyMediaLite is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// MyMediaLite is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with MyMediaLite.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Globalization;
using MyMediaLite.Data;
using MyMediaLite.DataType;

namespace MyMediaLite.RatingPrediction
{
	/// <summary>Social-network-aware matrix factorization</summary>
	/// <remarks>
	/// This implementation assumes a binary trust network.
	///
	/// Mohsen Jamali, Martin Ester:
    /// A matrix factorization technique with trust propagation for recommendation in social networks
    /// RecSys '10: Proceedings of the Fourth ACM Conference on Recommender Systems, 2010
	/// </remarks>
	public class SigmoidSocialMF : BiasedMatrixFactorization, IUserRelationAwareRecommender
	{
		// TODO
		//  - MAE optimization or throw Exception
		//  - bold-driver support or throw Exception
		//  - frequency-based regularization

		/// <summary>Social network regularization constant</summary>
		public float SocialRegularization { get { return social_regularization; } set { social_regularization = value; } }
		private float social_regularization = 1;

		/*
		/// <summary>
		/// Use stochastic gradient descent instead of batch gradient descent
		/// </summary>
		public bool StochasticLearning { get; set; }
		*/

		///
		public SparseBooleanMatrix UserRelation { get { return this.user_connections; } set {	this.user_connections = value; } }
		private SparseBooleanMatrix user_connections;

		/// <summary>the number of users</summary>
		public int NumUsers { get { return MaxUserID + 1; } }

		///
		protected override void InitModel()
		{
			this.MaxUserID = Math.Max(MaxUserID, user_connections.NumberOfRows - 1);
			this.MaxUserID = Math.Max(MaxUserID, user_connections.NumberOfColumns - 1);

			// init latent factor matrices
			user_factors = new Matrix<float>(MaxUserID + 1, NumFactors);
			item_factors = new Matrix<float>(MaxItemID + 1, NumFactors);
			MatrixExtensions.InitNormal(user_factors, InitMean, InitStdDev);
			MatrixExtensions.InitNormal(item_factors, InitMean, InitStdDev);
			// init biases
			user_bias = new float[MaxUserID + 1];
			item_bias = new float[MaxItemID + 1];
		}

		///
		public override void Train()
		{
			InitModel();

			rating_range_size = max_rating - min_rating;

			// learn model parameters
			double avg = (ratings.Average - min_rating) / rating_range_size;
			global_bias = (float) Math.Log(avg / (1 - avg));

			for (int current_iter = 0; current_iter < NumIter; current_iter++)
				Iterate(ratings.RandomIndex, true, true);
		}

		///
		protected override void Iterate(IList<int> rating_indices, bool update_user, bool update_item)
		{
			// We ignore the method's arguments. FIXME
			IterateBatch();
		}

		private void IterateBatch()
		{
			// I. compute gradients
			var user_factors_gradient = new Matrix<float>(user_factors.dim1, user_factors.dim2);
			var item_factors_gradient = new Matrix<float>(item_factors.dim1, item_factors.dim2);
			var user_bias_gradient    = new float[user_factors.dim1];
			var item_bias_gradient    = new float[item_factors.dim1];

			// I.1 prediction error
			for (int index = 0; index < ratings.Count; index++)
			{
				int user_id = ratings.Users[index];
				int item_id = ratings.Items[index];

				// prediction
				float score = global_bias;
				score += user_bias[user_id];
				score += item_bias[item_id];
				for (int f = 0; f < NumFactors; f++)
					score += user_factors[user_id, f] * item_factors[item_id, f];
				double sig_score = 1 / (1 + Math.Exp(-score));

				float prediction = (float) (MinRating + sig_score * rating_range_size);
				float error      = ratings[index] - prediction;

				double gradient_common = error * sig_score * (1 - sig_score) * rating_range_size;

				// add up error gradient
				for (int f = 0; f < NumFactors; f++)
				{
					float u_f = user_factors[user_id, f];
					float i_f = item_factors[item_id, f];

					user_factors_gradient.Inc(user_id, f, gradient_common * i_f);
					item_factors_gradient.Inc(item_id, f, gradient_common * u_f);
				}
			}

			// I.2 L2 regularization
			//        biases
			for (int u = 0; u < user_bias_gradient.Length; u++)
				user_bias_gradient[u] -= user_bias[u] * RegU * BiasReg;
			for (int i = 0; i < item_bias_gradient.Length; i++)
				item_bias_gradient[i] -= item_bias[i] * RegI * BiasReg;
			//        latent factors
			for (int u = 0; u < user_factors_gradient.dim1; u++)
				for (int f = 0; f < NumFactors; f++)
					user_factors_gradient.Inc(u, f, user_factors[u, f] * -RegU);

			for (int i = 0; i < item_factors_gradient.dim1; i++)
				for (int f = 0; f < NumFactors; f++)
					item_factors_gradient.Inc(i, f, item_factors[i, f] * -RegI);

			// I.3 social network regularization
			if (SocialRegularization != 0)
				for (int u = 0; u < user_factors_gradient.dim1; u++)
				{
					// see eq. (13) in the paper
					float[] sum_connections    = new float[NumFactors];
					float bias_sum_connections = 0;
					int num_connections        = user_connections[u].Count;

					// user bias part
					foreach (int v in user_connections[u])
						bias_sum_connections += user_bias[v];
					if (num_connections != 0)
						user_bias_gradient[u] -= social_regularization * (user_bias[u] - bias_sum_connections / num_connections);
					foreach (int v in user_connections[u])
						if (user_connections[v].Count != 0)
						{
							float trust_v = (float) 1 / user_connections[v].Count;
							float diff = 0;
							foreach (int w in user_connections[v])
								diff -= user_bias[w];

							diff = diff * trust_v;
							diff += user_bias[v];

							if (num_connections != 0)
								user_bias_gradient[u] -= social_regularization * trust_v * diff / num_connections;
						}

					// latent factor part
					foreach (int v in user_connections[u])
						for (int f = 0; f < NumFactors; f++)
							sum_connections[f] += user_factors[v, f];
					if (num_connections != 0)
						for (int f = 0; f < NumFactors; f++)
							user_factors_gradient.Inc(u, f, -social_regularization * (user_factors[u, f] - sum_connections[f] / num_connections));
					foreach (int v in user_connections[u])
						if (user_connections[v].Count != 0)
						{
							float trust_v = (float) 1 / user_connections[v].Count;
							for (int f = 0; f < NumFactors; f++)
							{
								float diff = 0;
								foreach (int w in user_connections[v])
									diff -= user_factors[w, f];
								diff = diff * trust_v;
								diff += user_factors[v, f];
								if (num_connections != 0)
									user_factors_gradient.Inc(u, f, -social_regularization * trust_v * diff / num_connections);
							}
						}
				}

			// II. apply gradient descent step
			for (int u = 0; u < user_factors_gradient.dim1; u++)
			{
				user_bias[u] += (float) (user_bias_gradient[u] * LearnRate * BiasLearnRate);
				for (int f = 0; f < NumFactors; f++)
					MatrixExtensions.Inc(user_factors, u, f, user_factors_gradient[u, f] * LearnRate);
			}
			for (int i = 0; i < item_factors_gradient.dim1; i++)
			{
				item_bias[i] += (float) (item_bias_gradient[i] * LearnRate * BiasLearnRate);
				for (int f = 0; f < NumFactors; f++)
					MatrixExtensions.Inc(item_factors, i, f, item_factors_gradient[i, f] * LearnRate);
			}
		}

		///
		public override float ComputeObjective()
		{
			double loss = 0;

			for (int i = 0; i < ratings.Count; i++)
			{
				int user_id = ratings.Users[i];
				int item_id = ratings.Items[i];
				loss += Math.Pow(Predict(user_id, item_id) - ratings[i], 2);
			}

			double complexity = 0;
			for (int u = 0; u <= MaxUserID; u++)
				if (ratings.CountByUser.Count > u)
				{
					complexity += RegU * Math.Pow(VectorExtensions.EuclideanNorm(user_factors.GetRow(u)), 2);
					complexity += BiasReg * Math.Pow(user_bias[u], 2);
				}
			for (int i = 0; i <= MaxItemID; i++)
				if (ratings.CountByItem.Count > i)
				{
					complexity += RegI * Math.Pow(VectorExtensions.EuclideanNorm(item_factors.GetRow(i)), 2);
					complexity += BiasReg * Math.Pow(item_bias[i], 2);
				}

			// TODO add penality term for neighborhood regularization

			return (float) (loss + complexity);
		}

		///
		public override string ToString()
		{
			return string.Format(
				CultureInfo.InvariantCulture,
				"{0} num_factors={1} reg_u={2} reg_i={3} bias_reg={4} social_regularization={5} learn_rate={6} bias_learn_rate={7} num_iter={8}",
				this.GetType().Name, NumFactors, RegU, RegI, BiasReg, SocialRegularization, LearnRate, BiasLearnRate, NumIter);
		}
	}
}
