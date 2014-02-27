using System;
using System.Collections.Generic;
using QuickGraph;
using System.Windows;

namespace GraphSharp.Algorithms.Layout.Simple.FDP
{
    public class KKLayoutAlgorithm<TVertex, TEdge, TGraph> : DefaultParameterizedLayoutAlgorithmBase<TVertex, TEdge, TGraph, KKLayoutParameters>
        where TVertex : class
        where TEdge : IEdge<TVertex>
        where TGraph : IBidirectionalGraph<TVertex, TEdge>
    {

        #region Variables needed for the layout
        /// <summary>
        /// Minimal distances between the vertices.
        /// </summary>
        private double[,] _distances;
        private double[,] _edgeLengths;
        private double[,] _springConstants;

        //cache for speed-up
        private TVertex[] _vertices;
        /// <summary>
        /// Positions of the vertices, stored by indices.
        /// </summary>
        private Point[] _positions;

        private double _diameter;
        private double _idealEdgeLength;
        #endregion

        #region Contructors
        public KKLayoutAlgorithm(TGraph visitedGraph, KKLayoutParameters oldParameters)
            : this(visitedGraph, null, oldParameters) { }

        public KKLayoutAlgorithm(TGraph visitedGraph, IDictionary<TVertex, Point> vertexPositions, KKLayoutParameters oldParameters)
            : base(visitedGraph, vertexPositions, oldParameters) { }
        #endregion

        protected override void InternalCompute()
        {
            #region Initialization
            _distances = new double[VisitedGraph.VertexCount, VisitedGraph.VertexCount];
            _edgeLengths = new double[VisitedGraph.VertexCount, VisitedGraph.VertexCount];
            _springConstants = new double[VisitedGraph.VertexCount, VisitedGraph.VertexCount];
            _vertices = new TVertex[VisitedGraph.VertexCount];
            _positions = new Point[VisitedGraph.VertexCount];

            //initializing with random positions
            InitializeWithRandomPositions( Parameters.Width, Parameters.Height );

            //copy positions into array (speed-up)
            int index = 0;
            foreach ( var v in VisitedGraph.Vertices )
            {
                _vertices[index] = v;
                _positions[index] = VertexPositions[v];
                index++;
            }

            //calculating the diameter of the graph
            //TODO check the diameter algorithm
            _diameter = VisitedGraph.GetDiameter<TVertex, TEdge, TGraph>( out _distances );

            //L0 is the length of a side of the display area
            double L0 = Math.Min( Parameters.Width, Parameters.Height );

            //ideal length = L0 / max d_i,j
            _idealEdgeLength = ( L0 / _diameter ) * Parameters.LengthFactor;

            //calculating the ideal distance between the nodes
            for ( int i = 0; i < VisitedGraph.VertexCount - 1; i++ )
            {
                for ( int j = i + 1; j < VisitedGraph.VertexCount; j++ )
                {
                    //distance between non-adjacent vertices
                    double dist = _diameter * Parameters.DisconnectedMultiplier;

                    //calculating the minimal distance between the vertices
                    if ( _distances[i, j] != double.MaxValue )
                        dist = Math.Min( _distances[i, j], dist );
                    if ( _distances[j, i] != double.MaxValue )
                        dist = Math.Min( _distances[j, i], dist );
                    _distances[i, j] = _distances[j, i] = dist;
                    _edgeLengths[i, j] = _edgeLengths[j, i] = _idealEdgeLength * dist;
                    _springConstants[i, j] = _springConstants[j, i] = Parameters.K / Math.Pow( dist, 2 );
                }
            }
            #endregion

            int n = VisitedGraph.VertexCount;
            if ( n == 0 )
                return;

            //TODO check this condition
            for ( int currentIteration = 0; currentIteration < Parameters.MaxIterations; currentIteration++ )
            {
                #region An iteration
                double maxDeltaM = double.NegativeInfinity;
                int pm = -1;

                //get the 'p' with the max delta_m
                for ( int i = 0; i < n; i++ )
                {
                    double deltaM = CalculateEnergyGradient( i );
                    if ( maxDeltaM < deltaM )
                    {
                        maxDeltaM = deltaM;
                        pm = i;
                    }
                }
                //TODO is needed?
                if ( pm == -1 )
                    return;

                //calculating the delta_x & delta_y with the Newton-Raphson method
                //there is an upper-bound for the while (deltaM > epsilon) {...} cycle (100)
                for ( int i = 0; i < 100; i++ )
                {
                    _positions[pm] += CalcDeltaXY( pm );

                    double deltaM = CalculateEnergyGradient( pm );
                    //real stop condition
                    if ( deltaM < double.Epsilon )
                        break;
                }

                //what if some of the vertices would be exchanged?
                if ( Parameters.ExchangeVertices && maxDeltaM < double.Epsilon )
                {
                    double energy = CalcEnergy();
                    for ( int i = 0; i < n - 1; i++ )
                    {
                        for ( int j = i + 1; j < n; j++ )
                        {
                            double xenergy = CalcEnergyIfExchanged( i, j );
                            if ( energy > xenergy )
                            {
                                Point p = _positions[i];
                                _positions[i] = _positions[j];
                                _positions[j] = p;
                                return;
                            }
                        }
                    }
                }
                #endregion

                if ( ReportOnIterationEndNeeded )
                    Report( currentIteration );
            }
            Report( Parameters.MaxIterations );
        }

        protected void Report( int currentIteration )
        {
            #region Copy the calculated positions
            //poz�ci�k �tm�sol�sa a VertexPositions-ba
            for ( int i = 0; i < _vertices.Length; i++ )
                VertexPositions[_vertices[i]] = _positions[i];
            #endregion

            OnIterationEnded( currentIteration, (double)currentIteration / (double)Parameters.MaxIterations, "Iteration " + currentIteration + " finished.", true );
        }

        /// <returns>
        /// Calculates the energy of the state where 
        /// the positions of the vertex 'p' & 'q' are exchanged.
        /// </returns>
        private double CalcEnergyIfExchanged( int p, int q )
        {
            double energy = 0;
            for ( int i = 0; i < _vertices.Length - 1; i++ )
            {
                for ( int j = i + 1; j < _vertices.Length; j++ )
                {
                    int ii = ( i == p ) ? q : i;
                    int jj = ( j == q ) ? p : j;

                    double l_ij = _edgeLengths[i, j];
                    double k_ij = _springConstants[i, j];
                    double dx = _positions[ii].X - _positions[jj].X;
                    double dy = _positions[ii].Y - _positions[jj].Y;

                    energy += k_ij / 2 * ( dx * dx + dy * dy + l_ij * l_ij -
                                           2 * l_ij * Math.Sqrt( dx * dx + dy * dy ) );
                }
            }
            return energy;
        }

        /// <summary>
        /// Calculates the energy of the spring system.
        /// </summary>
        /// <returns>Returns with the energy of the spring system.</returns>
        private double CalcEnergy()
        {
            double energy = 0, dist, l_ij, k_ij, dx, dy;
            for ( int i = 0; i < _vertices.Length - 1; i++ )
            {
                for ( int j = i + 1; j < _vertices.Length; j++ )
                {
                    dist = _distances[i, j];
                    l_ij = _edgeLengths[i, j];
                    k_ij = _springConstants[i, j];

                    dx = _positions[i].X - _positions[j].X;
                    dy = _positions[i].Y - _positions[j].Y;

                    energy += k_ij / 2 * ( dx * dx + dy * dy + l_ij * l_ij -
                                           2 * l_ij * Math.Sqrt( dx * dx + dy * dy ) );
                }
            }
            return energy;
        }

        /// <summary>
        /// Determines a step to new position of the vertex m.
        /// </summary>
        /// <returns></returns>
        private Vector CalcDeltaXY( int m )
        {
            double dxm = 0, dym = 0, d2xm = 0, dxmdym = 0, dymdxm = 0, d2ym = 0;
            double l, k, dx, dy, d, ddd;

            for ( int i = 0; i < _vertices.Length; i++ )
            {
                if ( i != m )
                {
                    //common things
                    l = _edgeLengths[m, i];
                    k = _springConstants[m, i];
                    dx = _positions[m].X - _positions[i].X;
                    dy = _positions[m].Y - _positions[i].Y;

                    //distance between the points
                    d = Math.Sqrt( dx * dx + dy * dy );
                    ddd = Math.Pow( d, 3 );

                    dxm += k * ( 1 - l / d ) * dx;
                    dym += k * ( 1 - l / d ) * dy;
                    //TODO isn't it wrong?
                    d2xm += k * ( 1 - l * Math.Pow( dy, 2 ) / ddd );
                    //d2E_d2xm += k_mi * ( 1 - l_mi / d + l_mi * dx * dx / ddd );
                    dxmdym += k * l * dx * dy / ddd;
                    //d2E_d2ym += k_mi * ( 1 - l_mi / d + l_mi * dy * dy / ddd );
                    //TODO isn't it wrong?
                    d2ym += k * ( 1 - l * Math.Pow( dx, 2 ) / ddd );
                }
            }
            // d2E_dymdxm equals to d2E_dxmdym
            dymdxm = dxmdym;

            double denomi = d2xm * d2ym - dxmdym * dymdxm;
            double deltaX = ( dxmdym * dym - d2ym * dxm ) / denomi;
            double deltaY = ( dymdxm * dxm - d2xm * dym ) / denomi;
            return new Vector( deltaX, deltaY );
        }

        /// <summary>
        /// Calculates the gradient energy of a vertex.
        /// </summary>
        /// <param name="m">The index of the vertex.</param>
        /// <returns>Calculates the gradient energy of the vertex <code>m</code>.</returns>
        private double CalculateEnergyGradient( int m )
        {
            double dxm = 0, dym = 0, dx, dy, d, common;
            //        {  1, if m < i
            // sign = { 
            //        { -1, if m > i
            for ( int i = 0; i < _vertices.Length; i++ )
            {
                if ( i == m )
                    continue;

                //differences of the positions
                dx = ( _positions[m].X - _positions[i].X );
                dy = ( _positions[m].Y - _positions[i].Y );

                //distances of the two vertex (by positions)
                d = Math.Sqrt( dx * dx + dy * dy );

                common = _springConstants[m, i] * ( 1 - _edgeLengths[m, i] / d );
                dxm += common * dx;
                dym += common * dy;
            }
            // delta_m = sqrt((dE/dx)^2 + (dE/dy)^2)
            return Math.Sqrt( dxm * dxm + dym * dym );
        }
    }
}