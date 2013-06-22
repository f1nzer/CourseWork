﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using CourseWork.Manager;
using CourseWork.Templates;
using CourseWork.Utilities.Helpers;
using GMap.NET;
using GMap.NET.WindowsPresentation;

namespace CourseWork.Maps
{
    public class MapHelper
    {
        private static MapHelper _instance;

        public static MapHelper SetInstance(GMapControl map)
        {
            return _instance ?? (_instance = new MapHelper(map));
        }

        public static MapHelper Instance
        {
            get
            {
                if (_instance == null) throw new Exception("Используй инициализацию с параметром");
                return _instance;
            }
        }

        private static GMapControl _map;
        private MapHelper(GMapControl map)
        {
            _map = map;
            map.Loaded += (o, args) => ReDrawElements();
            map.SizeChanged += (o, args) => ReDrawElements();
            map.OnMapZoomChanged += ReDrawElements;
            map.OnMapDrag += ReDrawElements;
        }

        /// <summary>
        /// Обновить положение элемента относительно экранных координат
        /// </summary>
        /// <param name="item"></param>
        public void UpdateLatLngPoses(DiagramItem item)
        {
            item.PositionLatLng = _map.FromLocalToLatLng(Convert.ToInt32(item.CenterPoint.X),
                                                         Convert.ToInt32(item.CenterPoint.Y));
        }

        /// <summary>
        /// Обновить положение элемента на экрани относительно его LatLng
        /// </summary>
        /// <param name="item"></param>
        public void UpdateScreenCoords(DiagramItem item)
        {
            var point = _map.FromLatLngToLocal(item.PositionLatLng);
            item.CenterPoint = new Point(point.X, point.Y);
        }

        /// <summary>
        /// Авторазмер карты для отображения всех элементов
        /// </summary>
        public void FitMapToScreen()
        {
            var lat = DiagramItemManager.Instance.Items.Max(x => x.PositionLatLng.Lat);
            var lng = DiagramItemManager.Instance.Items.Min((x => x.PositionLatLng.Lng));
            var heightLat = lat - DiagramItemManager.Instance.Items.Min((x => x.PositionLatLng.Lat));
            var widthLng = DiagramItemManager.Instance.Items.Max(x => x.PositionLatLng.Lng) - lng;
            _map.SetZoomToFitRect(new RectLatLng(lat, lng, widthLng, heightLat));
        }

        /// <summary>
        /// Перерисовать элементы на карте
        /// </summary>
        public void ReDrawElements()
        {
            foreach (var diagramItem in DiagramItemManager.Instance.Items)
            {
                var point = _map.FromLatLngToLocal(diagramItem.PositionLatLng);
                diagramItem.Move(point.X, point.Y);
            }
        }

        /// <summary>
        /// Приблизить/отдалить карту
        /// </summary>
        /// <param name="delta">размер приближения</param>
        public void Zoom(int delta)
        {

            // приближаем - двигаем к курсору; отодвигаем - оставляем на месте
            if (delta > 0)
            {
                _map.Position = _map.FromLocalToLatLng(Convert.ToInt32(Mouse.GetPosition(_map).X),
                                                         Convert.ToInt32(Mouse.GetPosition(_map).Y));
            }
            _map.Zoom += delta;
        }

        /// <summary>
        /// Проверка на порог расстояния между элементами
        /// </summary>
        public void CheckDistance()
        {
            const int replDistance = 40;
            _lookableItems =
                DiagramItemManager.Instance.Items.Where(x => x.DiagramItemType != DiagramItemType.Group).ToList();

            _distMatrix = new int[_lookableItems.Count, _lookableItems.Count];
            _chMatrix = new int[_lookableItems.Count, _lookableItems.Count];
            for (int i = 0; i < _lookableItems.Count - 1; i++)
            {
                for (int j = i + 1; j < _lookableItems.Count; j++)
                {
                    var distance = MathHelper.Distance(_lookableItems[i].CenterPoint, _lookableItems[j].CenterPoint);
                    _distMatrix[i, j] = distance < replDistance ? 1 : 0;
                }
            }

            for (int i = 0; i < _lookableItems.Count; i++)
            {
                var currentGroup = new List<DiagramItem>();
                DFS(i, currentGroup);

                // текущую группу необходимо сгруппировать
                if (currentGroup.Count > 1)
                {
                    // средняя точка всей группы
                    var centerPoint = new Point(currentGroup.Sum(x => x.PositionLatLng.Lat) / currentGroup.Count,
                                                currentGroup.Sum(x => x.PositionLatLng.Lng) / currentGroup.Count);
                    var group =
                        (GroupDevices)DiagramItemManager.Instance.AddNewItem(DiagramItemType.Group, new Point());
                    group.PositionLatLng = new PointLatLng(centerPoint.X, centerPoint.Y);
                    UpdateScreenCoords(group);

                    currentGroup.ForEach(@group.Add);
                    group.Compose();
                }
            }

            UpdateGroupsConnections();
        }

        private int[,] _distMatrix, _chMatrix;
        private List<DiagramItem> _lookableItems;
        /// <summary>
        /// Просмотр в глубину матрицы весов расстояний
        /// </summary>
        /// <param name="i"></param>
        /// <param name="currentGroup"></param>
        private void DFS(int i, List<DiagramItem> currentGroup)
        {
            for (int j = 0; j < _distMatrix.GetLength(0); j++)
            {
                if (_chMatrix[i, j] == 1) continue;

                _chMatrix[i, j] = 1;
                _chMatrix[j, i] = 1;

                if (_distMatrix[i, j] == 1)
                {
                    if (!currentGroup.Contains(_lookableItems[i])) currentGroup.Add(_lookableItems[i]);
                    if (!currentGroup.Contains(_lookableItems[j])) currentGroup.Add(_lookableItems[j]);

                    DFS(j, currentGroup);
                }
            }
        }

        private void UpdateGroupsConnections()
        {
            var tuples = new List<Tuple<DiagramItem, DiagramItem>>();
            foreach (var group in DiagramItemManager.Instance.GroupDeviceses)
            {
                foreach (var diagramItem in group.GetItems())
                {
                    foreach (var connectionArrow in diagramItem.ConnectionArrows)
                    {
                        // связь не внутри одной группы
                        if (group.GetItems().Contains(connectionArrow.TargetItem) &&
                            group.GetItems().Contains(connectionArrow.FromItem)) continue;

                        // связь соединяет блоки в группах
                        if (connectionArrow.TargetItem.Visibility == Visibility.Hidden &&
                            connectionArrow.FromItem.Visibility == Visibility.Hidden)
                        {
                            tuples.Add(new Tuple<DiagramItem, DiagramItem>(
                                           DiagramItemManager.Instance.GroupDeviceses.Single(
                                               x => x.GetItems().Contains(connectionArrow.FromItem)),
                                           DiagramItemManager.Instance.GroupDeviceses.Single(
                                               x => x.GetItems().Contains(connectionArrow.TargetItem))));
                        }
                        else
                        {
                            // связь между группой и отдельным блоком
                            var fGroup =
                                DiagramItemManager.Instance.GroupDeviceses.SingleOrDefault(
                                    x => x.GetItems().Contains(connectionArrow.FromItem));
                            var item = connectionArrow.TargetItem;
                            if (fGroup == null)
                            {
                                fGroup = DiagramItemManager.Instance.GroupDeviceses.SingleOrDefault(
                                    x => x.GetItems().Contains(connectionArrow.TargetItem));
                                item = connectionArrow.FromItem;
                                tuples.Add(new Tuple<DiagramItem, DiagramItem>(item, fGroup));
                            }
                            else
                            {
                                tuples.Add(new Tuple<DiagramItem, DiagramItem>(fGroup, item));
                            }
                        }
                    }
                }
            }

            var result = tuples.Distinct();
            foreach (var tuple in result)
            {
                DiagramItemManager.Instance.AddNewLink(tuple.Item1, tuple.Item2, 0, true);
            }
        }
    }
}
