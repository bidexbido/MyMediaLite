// Copyright (C) 2010, 2011, 2012 Zeno Gantner
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
using MyMediaLite.Util;

namespace MyMediaLite.DataType
{
	/// <summary>Class for storing sparse matrices</summary>
	/// <remarks>
	/// The data is stored in row-major mode.
	/// Indexes are zero-based.
	/// Access is internally done by binary search.
	/// </remarks>
	/// <typeparam name="T">the matrix element type, must have a default constructor/value</typeparam>
	public class SparseMatrix<T> : IMatrix<T> where T:new()
	{
		/// <summary>List of lists that stores the values of the entries</summary>
		protected internal List<List<T>> value_list = new List<List<T>>();
		
		/// <summary>List of lists that stores the column indices of the entries</summary>
		protected internal List<List<int>> index_list = new List<List<int>>();
		
		///
		public virtual bool IsSymmetric
		{
			get {
				if (NumberOfRows != NumberOfColumns)
					return false;
				for (int i = 0; i < index_list.Count; i++)
					foreach (var j in index_list[i])
					{
						if (i > j)
							continue; // check every pair only once

						if (! this[i, j].Equals(this[j, i]))
							return false;
					}
				return true;
			}
		}

		///
		public int NumberOfRows { get { return index_list.Count; } }

		///
		public int NumberOfColumns { get; private set; }

		/// <summary>Get a row of the matrix</summary>
		/// <param name="x">the row ID</param>
		public Dictionary<int, T> this [int x]
		{
			get {
				throw new NotImplementedException();
			}
		}

		/// <summary>Access the elements of the sparse matrix</summary>
		/// <param name="x">the row ID</param>
		/// <param name="y">the column ID</param>
		public virtual T this [int x, int y]
		{
			get {
				if (x < index_list.Count)
				{
					int index = index_list[x].BinarySearch(y);
					if (index >= 0)
						return value_list[x][index];
				}
				return new T();
			}
			set {
				if (x >= index_list.Count)
					for (int i = index_list.Count; i <= x; i++)
					{
						index_list.Add( new List<int>() );
						value_list.Add( new List<T>() );
					}
				
				int index = index_list[x].BinarySearch(y);
				if (index >= 0)
					value_list[x][index] = value;
				else
				{
					int new_index = ~index;
					index_list[x].Insert(new_index, y);
					value_list[x].Insert(new_index, value);
				}
			}
		}

		/// <summary>The row and column IDs of non-empty entries in the matrix</summary>
		/// <value>The row and column IDs of non-empty entries in the matrix</value>
		public virtual IList<Pair<int, int>> NonEmptyEntryIDs
		{
			get {
				var return_list = new List<Pair<int, int>>();
				for (int row_id = 0; row_id < index_list.Count; row_id++)
					foreach (var col_id in index_list[row_id])
						return_list.Add(new Pair<int, int>(row_id, col_id));
				return return_list;
			}
		}

		/// <summary>The number of non-empty entries in the matrix</summary>
		/// <value>The number of non-empty entries in the matrix</value>
		public virtual int NumberOfNonEmptyEntries
		{
			get {
				int counter = 0;
				foreach (var row in index_list)
					counter += row.Count;
				return counter;
			}
		}

		/// <summary>Create a sparse matrix with a given number of rows</summary>
		/// <param name="num_rows">the number of rows</param>
		/// <param name="num_cols">the number of columns</param>
		public SparseMatrix(int num_rows, int num_cols)
		{
			for (int i = 0; i < num_rows; i++)
			{
				index_list.Add( new List<int>() );
				value_list.Add( new List<T>() );
			}
			NumberOfColumns = num_cols;
		}

		///
		public virtual IMatrix<T> CreateMatrix(int num_rows, int num_columns)
		{
			return new SparseMatrix<T>(num_rows, num_columns);
		}

		///
		public void Grow(int num_rows, int num_cols)
		{
			// if necessary, grow rows
			if (num_rows > NumberOfRows)
				for (int i = index_list.Count; i < num_rows; i++)
				{
					index_list.Add( new List<int>() );
					value_list.Add( new List<T>() );
				}

			// if necessary, grow columns
			if (num_cols > NumberOfColumns)
				NumberOfColumns = num_cols;
		}

		///
		public virtual IMatrix<T> Transpose()
		{
			var transpose = new SparseMatrix<T>(NumberOfColumns, NumberOfRows);
			foreach (Pair<int, int> p in NonEmptyEntryIDs)
				transpose[p.Second, p.First] = this[p.First, p.Second];
			return transpose;
		}
	}
}